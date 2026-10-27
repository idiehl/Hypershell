using Hypershell;
using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

/// <summary>Exercises standalone authoring in private profiles and a bounded library copy.</summary>
public static class StandaloneStudioVerifier
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    { ".git", ".hyperlib", ".hypershell", "bin", "obj", "node_modules", "app", "output", "artifacts", "publish", ".vs" };

    public static async Task VerifyAsync(string outputDirectory, string repositoryRoot)
    {
        var repository = Path.GetFullPath(repositoryRoot);
        var root = Path.Combine(Path.GetFullPath(outputDirectory), "standalone-studio-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(repository, "Hyperlib");
        if (!File.Exists(Path.Combine(source, VisualPageProjectService.ProjectManifestFileName)))
            throw new InvalidOperationException("The repository must contain the HyperLib authoring fixture.");
        if (root.StartsWith(Path.TrimEndingDirectorySeparator(source) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Standalone verification output must be outside the source library.");
        Directory.CreateDirectory(root);
        var library = Path.Combine(root, "library");
        var report = new Receipt { Fixture = root, SourceLibrary = source };
        var sourceBefore = Fingerprint(source);
        var environmentNames = new[]
        {
            SettingsService.ApplicationDataOverrideVariable,
            SettingsService.SharedMyNetDirectoryOverrideVariable,
            StandaloneStudioHost.BaseDataOverrideVariable,
            "HYPERLIB_ROOT",
            "HYPERSHELL_RELAUNCH_COMMAND", "HYPERSHELL_RELAUNCH_DISPLAY_NAME", "HYPERSHELL_RELAUNCH_ICON_RESOURCE",
            "HYPERSHELL_APP_USER_MODEL_ID"
        };
        var previousEnvironment = environmentNames.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            CopySource(source, library);
            var libraryBefore = Fingerprint(library);
            var originalRoot = Path.Combine(root, "original-shell-profile");
            Environment.SetEnvironmentVariable(SettingsService.ApplicationDataOverrideVariable, originalRoot);
            Environment.SetEnvironmentVariable(SettingsService.SharedMyNetDirectoryOverrideVariable, null);
            Environment.SetEnvironmentVariable(StandaloneStudioHost.BaseDataOverrideVariable, originalRoot);
            Environment.SetEnvironmentVariable("HYPERLIB_ROOT", library);
            var original = new SettingsService(originalRoot);
            var originalSettings = original.Load();
            // Explicitly enable the shell's terminal to verify the mode overrides it at runtime.
            originalSettings.Application.CreateTerminalOnStartup = true;
            originalSettings.Explorer.StartupDirectory = root;
            original.Save(originalSettings);
            await File.WriteAllTextAsync(original.EditorSessionPath, "{\"Documents\":[],\"ActiveDocumentSessionId\":\"original-shell-marker\"}");
            await File.WriteAllTextAsync(original.VisualPageStudioSessionPath, "{\"schemaVersion\":1,\"projects\":[],\"activeProjectId\":\"original-studio-marker\"}");
            Directory.CreateDirectory(original.EditorRecoveryDirectory);
            await File.WriteAllTextAsync(Path.Combine(original.EditorRecoveryDirectory, "keep-original.txt"), "An existing shell draft must remain intact.");
            await File.WriteAllTextAsync(original.WorkspaceAiProviderConfigurationPath, "{}");
            var protectedFiles = new[]
            {
                original.SettingsPath, original.EditorSessionPath, original.VisualPageStudioSessionPath,
                original.WorkspaceAiProviderConfigurationPath, Path.Combine(original.EditorRecoveryDirectory, "keep-original.txt")
            }.ToDictionary(path => path, File.ReadAllBytes);
            var legacyRoot = Path.Combine(originalRoot, "Standalone", "hyperlib-editor");
            var legacySettings = new SettingsService(legacyRoot);
            Directory.CreateDirectory(legacySettings.EditorRecoveryDirectory);
            await File.WriteAllTextAsync(legacySettings.SettingsPath, "{\"legacyProfileSentinel\":true}");
            await File.WriteAllTextAsync(legacySettings.EditorSessionPath, "{\"Documents\":[],\"ActiveDocumentSessionId\":\"keep-legacy-document-session\"}");
            await File.WriteAllTextAsync(legacySettings.VisualPageStudioSessionPath, "{\"schemaVersion\":1,\"projects\":[],\"activeProjectId\":\"keep-legacy-editor-session\"}");
            await File.WriteAllTextAsync(Path.Combine(legacySettings.EditorRecoveryDirectory, "legacy-draft.txt"), "Keep this draft from the separate Hyperlib Editor profile.");
            var legacyProfileBefore = Fingerprint(legacyRoot);

            await RunStaAsync(async () =>
            {
                LoadResources(repository);
                StandaloneStudioHost.ConfigureProcess(StandaloneStudioMode.HyperpageStudio);
                var studioSettings = new SettingsService();
                Check(report, IsInside(studioSettings.ApplicationDataDirectory, originalRoot)
                    && !SamePath(studioSettings.ApplicationDataDirectory, originalRoot),
                    "Studio uses a child profile instead of the existing shell profile.");
                Check(report, SamePath(studioSettings.AtlasMyNetAppsPath, original.AtlasMyNetAppsPath)
                    && SamePath(studioSettings.AtlasMyNetTrustPath, original.AtlasMyNetTrustPath),
                    "Studio resolves the existing MyNet registry and command trust while isolating authoring state.");
                StandaloneStudioHost.ConfigureProcess(StandaloneStudioMode.HyperpageStudio);
                Check(report, SamePath(new SettingsService().ApplicationDataDirectory, studioSettings.ApplicationDataDirectory),
                    "Repeated Studio configuration keeps the same profile instead of nesting another standalone directory.");
                using (var primary = new StandaloneStudioHost(StandaloneStudioMode.HyperpageStudio))
                using (var duplicate = new StandaloneStudioHost(StandaloneStudioMode.HyperpageStudio))
                using (var legacyAlias = new StandaloneStudioHost(StandaloneStudioMode.HyperlibEditor))
                {
                    Check(report, primary.IsPrimaryInstance && !duplicate.IsPrimaryInstance && !legacyAlias.IsPrimaryInstance,
                        "Studio and the legacy Editor launch flag share the same singleton and activate the same app.");
                }
                Check(report, StandaloneStudioHost.ParseMode(["--hyperlib-editor"]) == StandaloneStudioMode.HyperpageStudio
                    && StandaloneStudioHost.ParseMode(["--hyperpage-studio"]) == StandaloneStudioMode.HyperpageStudio,
                    "Both current and legacy launch arguments resolve to the unified Studio mode.");
                string projectRoot;
                string sourcePath;
                string unsavedDraft;
                using (var workspace = new MainWindowViewModel(studioSettings, StandaloneStudioMode.HyperpageStudio))
                {
                    await workspace.InitializeAsync();
                    await workspace.FileExplorer.WhenIdleAsync();
                    Check(report, workspace.IsStandaloneStudio && !workspace.IsProfileLoginGateVisible
                        && workspace.WorkspaceTabs.Count == 1 && workspace.ActiveWorkspaceTab is StudioProjectStartTabViewModel,
                        "Fresh standalone Studio opens one project start tab without sign-in or shell terminal tabs.");
                    var project = await workspace.CreateVisualPageProjectAsync(root, "Standalone authoring", new HyperpageProjectCreationOptions())
                        ?? throw new InvalidOperationException(workspace.StatusMessage);
                    projectRoot = project.ProjectRoot;
                    var document = await project.CreateProjectDocumentAsync("css", "standalone.css")
                        ?? throw new InvalidOperationException(project.StatusMessage);
                    document.Content = "/* Saved by standalone Studio. */\nbody { color: #2468ac; }\n";
                    Check(report, await project.SaveProjectAsync(), "The existing Studio source editor saves a newly created project and CSS document.");
                    sourcePath = Path.Combine(projectRoot, document.RelativePath);
                    Check(report, File.ReadAllText(sourcePath) == document.Content && !document.IsDirty,
                        "Saved standalone CSS reaches its real project file and clears dirty state.");
                    unsavedDraft = document.Content + "\n/* Keep this unfinished standalone draft. */";
                    document.Content = unsavedDraft;
                    await workspace.PrepareForShutdownAsync();
                    var savedSession = await new VisualPageStudioSessionService(studioSettings.VisualPageStudioSessionPath).LoadAsync();
                    Check(report, savedSession.Projects.Single().ProjectDocuments.Single(item => item.RelativePath == document.RelativePath).Content == unsavedDraft
                        && File.ReadAllText(sourcePath) != unsavedDraft,
                        "Shutdown persistence keeps the unfinished source in Studio recovery without silently saving it over disk.");
                }

                using (var reopened = new MainWindowViewModel(studioSettings, StandaloneStudioMode.HyperpageStudio))
                {
                    await reopened.InitializeAsync();
                    var project = reopened.WorkspaceTabs.OfType<VisualPageStudioTabViewModel>().Single();
                    var recovered = project.OpenProjectDocuments.Single(item => Path.GetFileName(item.RelativePath) == "standalone.css");
                    Check(report, SamePath(project.ProjectRoot, projectRoot) && recovered.Content == unsavedDraft
                        && recovered.IsDirty && recovered.IsRecovered && !reopened.IsProfileLoginGateVisible,
                        "A fresh standalone Studio instance restores its project and exact unfinished source draft.");
                    const string external = "/* A separate editor changed this file. */";
                    await File.WriteAllTextAsync(sourcePath, external);
                    Check(report, !await project.SaveProjectAsync() && File.ReadAllText(sourcePath) == external
                        && project.HasUnsavedChanges,
                        "Saving a recovered standalone draft refuses a newer external file and leaves the draft dirty.");
                    await reopened.PrepareForShutdownAsync();
                }
                var studioSessionBeforeAlias = File.ReadAllBytes(studioSettings.VisualPageStudioSessionPath);

                // The constructor and visual tree are real; no user desktop windows or native file pickers are shown.
                var studioWindow = new MainWindow(StandaloneStudioMode.HyperpageStudio);
                try
                {
                    var model = (MainWindowViewModel)studioWindow.DataContext;
                    Check(report, model.IsStandaloneStudio && model.WorkspaceTabs.All(tab => tab is not TerminalTabViewModel),
                        "The actual Studio window constructor creates the standalone view model without a terminal.");
                    Render(studioWindow, Path.Combine(root, "hyperpage-studio-home.png"), report);
                }
                finally { DisposeWindow(studioWindow); }

                StandaloneStudioHost.ConfigureProcess(StandaloneStudioMode.HyperlibEditor);
                var aliasSettings = new SettingsService();
                Check(report, SamePath(aliasSettings.ApplicationDataDirectory, studioSettings.ApplicationDataDirectory)
                    && SamePath(aliasSettings.EditorSessionPath, studioSettings.EditorSessionPath)
                    && SamePath(aliasSettings.VisualPageStudioSessionPath, studioSettings.VisualPageStudioSessionPath),
                    "The legacy Editor launch uses Studio's unified preference and recovery profile.");
                Check(report, studioSessionBeforeAlias.SequenceEqual(File.ReadAllBytes(studioSettings.VisualPageStudioSessionPath)),
                    "Resolving the legacy launch alias does not rewrite the current Studio recovery snapshot.");
                Check(report, SamePath(aliasSettings.AtlasMyNetAppsPath, original.AtlasMyNetAppsPath)
                    && SamePath(aliasSettings.AtlasMyNetTrustPath, original.AtlasMyNetTrustPath),
                    "The unified app retains the existing MyNet registry and command trust.");
                var aliasWindow = new MainWindow(StandaloneStudioMode.HyperlibEditor);
                try
                {
                    var alias = (MainWindowViewModel)aliasWindow.DataContext;
                    await alias.InitializeAsync();
                    var restored = alias.WorkspaceTabs.OfType<VisualPageStudioTabViewModel>().Single();
                    var retainedDraft = restored.OpenProjectDocuments.Single(item => Path.GetFileName(item.RelativePath) == "standalone.css");
                    Check(report, alias.IsStandaloneStudio && !alias.IsProfileLoginGateVisible
                        && alias.ActiveWorkspaceTab is StudioProjectStartTabViewModel
                        && SamePath(restored.ProjectRoot, projectRoot) && !SamePath(restored.ProjectRoot, library)
                        && alias.WorkspaceTabs.All(tab => tab is not TerminalTabViewModel),
                        "The legacy flag opens Studio's project start tab and existing project recovery without automatically loading the template library.");
                    Check(report, retainedDraft.Content == unsavedDraft && retainedDraft.IsDirty && retainedDraft.IsRecovered
                        && File.ReadAllText(sourcePath) == "/* A separate editor changed this file. */",
                        "Launching through the legacy alias preserves the unfinished Studio draft and the newer external source file.");
                    Render(aliasWindow, Path.Combine(root, "legacy-alias-studio-start.png"), report);
                    await alias.PrepareForShutdownAsync();
                    var saved = await new VisualPageStudioSessionService(aliasSettings.VisualPageStudioSessionPath).LoadAsync();
                    Check(report, saved.Projects.Single().ProjectDocuments.Single(item => Path.GetFileName(item.RelativePath) == "standalone.css").Content == unsavedDraft,
                        "The unified launch keeps the exact unfinished source in its shared Studio recovery snapshot.");
                }
                finally { DisposeWindow(aliasWindow); }
                Check(report, StandaloneStudioHost.GetAppUserModelId(StandaloneStudioMode.HyperpageStudio)
                        == StandaloneStudioHost.GetAppUserModelId(StandaloneStudioMode.HyperlibEditor)
                    && StandaloneStudioHost.GetDisplayName(StandaloneStudioMode.HyperpageStudio) == "Hyperpage Studio"
                    && StandaloneStudioHost.GetDisplayName(StandaloneStudioMode.HyperlibEditor) == "Hyperpage Studio",
                    "Both entry points use the unified Hyperpage Studio product name and taskbar identity.");
            });

            Check(report, protectedFiles.All(pair => File.Exists(pair.Key) && pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key))),
                "The unified app leaves original shell preferences, original sessions, AI settings, and existing recovery files byte-for-byte intact.");
            Check(report, Fingerprint(legacyRoot) == legacyProfileBefore,
                "The retired separate Editor profile, preferences, document session, and unfinished recovery sentinel remain byte-for-byte intact.");
            Check(report, Fingerprint(library) == libraryBefore,
                "Opening, rendering, and closing Studio through either entry point does not change copied template library source or content.");
            Check(report, Fingerprint(source) == sourceBefore,
                "All source repository library files remain byte-for-byte unchanged throughout verification.");
            report.Passed = true;
        }
        catch (Exception error) { report.Failure = error.ToString(); throw; }
        finally
        {
            foreach (var pair in previousEnvironment) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            await File.WriteAllTextAsync(Path.Combine(root, "standalone-studio.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"STANDALONE_STUDIO_OK|assertions:{report.Assertions.Count}|renders:{report.Renders.Count}|private-profiles|source-preserved|{root}");
    }

    private static void Check(Receipt report, bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        report.Assertions.Add(message);
    }

    private static void DisposeWindow(MainWindow window)
    {
        // This hidden construction test has no window-close prompt to answer. Dispose the same resources
        // as normal finalization after persistence has been tested explicitly above.
        typeof(MainWindow).GetMethod("FinalizeWindowClose", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(window, null);
    }

    private static void Render(MainWindow window, string path, Receipt report)
    {
        var surface = (FrameworkElement)window.Content;
        surface.Width = 1280; surface.Height = 800;
        surface.Measure(new Size(1280, 800));
        surface.Arrange(new Rect(0, 0, 1280, 800));
        surface.UpdateLayout();
        surface.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        surface.UpdateLayout();
        if (window.DataContext is MainWindowViewModel { ActiveWorkspaceTab: VisualPageStudioTabViewModel active })
        {
            var tabs = (System.Windows.Controls.TabControl)window.FindName("WorkspaceTabs");
            Check(report, Descendants<VisualPageStudioView>(surface).Any(view => view.ActualWidth > 400 && view.ActualHeight > 300),
                "The actual standalone window realizes a visible Studio content view. "
                + $"SelectedItem={tabs.SelectedItem?.GetType().Name ?? "null"}; SelectedContent={tabs.SelectedContent?.GetType().Name ?? "null"}; "
                + $"Active={active.Title}; containers={tabs.ItemContainerGenerator.Status}.");
        }
        else if (window.DataContext is MainWindowViewModel { ActiveWorkspaceTab: StudioProjectStartTabViewModel })
        {
            Check(report, Descendants<StudioProjectStartView>(surface).Any(view => view.ActualWidth > 400 && view.ActualHeight > 300),
                "The actual standalone window realizes the visible project start surface.");
        }
        var bitmap = new RenderTargetBitmap(1280, 800, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
        report.Renders.Add(path);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static bool SamePath(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
    private static bool IsInside(string candidate, string parent) => Path.GetFullPath(candidate).StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> Files(string root)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(root).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var attributes = File.GetAttributes(entry);
            if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("Fixture links are not supported: " + entry);
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                if (!Excluded.Contains(Path.GetFileName(entry))) foreach (var child in Files(entry)) yield return child;
            }
            else yield return entry;
        }
    }

    private static void CopySource(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        long bytes = 0;
        var count = 0;
        foreach (var file in Files(source))
        {
            if (++count > 30_000 || (bytes += new FileInfo(file).Length) > 768L * 1024 * 1024)
                throw new InvalidOperationException("Fixture exceeds the bounded library copy budget.");
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static string Fingerprint(string root)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Files(root))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file) + "\n"));
            using var input = File.OpenRead(file);
            hash.AppendData(SHA256.HashData(input));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static Task RunStaAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await action(); completion.TrySetResult(); }
                catch (Exception error) { completion.TrySetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Standalone Studio acceptance" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static void LoadResources(string repository)
    {
        if (Application.Current is not null)
        {
            if (!Application.Current.Dispatcher.CheckAccess()) throw new InvalidOperationException("Run standalone verification in its own process.");
            return;
        }
        var source = XDocument.Load(Path.Combine(repository, "src", "Hypershell", "App.xaml")).Root!;
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = new XElement(presentation + "ResourceDictionary",
            source.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Select(attribute => new XAttribute(attribute)),
            source.Element(presentation + "Application.Resources")!.Elements().Select(element => new XElement(element)));
        foreach (var attribute in dictionary.DescendantsAndSelf().Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            if (attribute.Value.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal) && !attribute.Value.Contains(";assembly=", StringComparison.Ordinal)) attribute.Value += ";assembly=Hypershell";
        foreach (var element in dictionary.DescendantsAndSelf())
            if (element.Name.NamespaceName.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal) && !element.Name.NamespaceName.Contains(";assembly=", StringComparison.Ordinal))
                element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=Hypershell");
        dictionary.Descendants().Attributes("Click").Remove();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
    }

    private sealed class Receipt
    {
        public bool Passed { get; set; }
        public string Fixture { get; set; } = "";
        public string SourceLibrary { get; set; } = "";
        public List<string> Assertions { get; } = [];
        public List<string> Renders { get; } = [];
        public string? Failure { get; set; }
    }
}
