using Hypershell;
using Hypershell.Models;
using Hypershell.Services;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

public static class ComboBoxDisplayVerifier
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    public static void Verify(string outputDirectory, string? appSourcePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Exception? failure = null;
        void Run()
        {
            try
            {
                VerifyOnSta(Path.GetFullPath(outputDirectory), Path.GetFullPath(appSourcePath ?? FindAppSource()));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            Run();
        }
        else
        {
            var thread = new Thread(Run) { IsBackground = true, Name = "ComboBox display verifier" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void VerifyOnSta(string outputDirectory, string sourcePath)
    {
        Directory.CreateDirectory(outputDirectory);
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (!app.Dispatcher.CheckAccess())
        {
            throw new InvalidOperationException("The existing WPF application belongs to another thread.");
        }

        var resourcesSource = ResourceSource(sourcePath);
        app.Resources = (ResourceDictionary)XamlReader.Parse(resourcesSource.ToString());
        var failures = new List<string>();
        var receipts = new List<CaseReceipt>();
        foreach (var width in new[] { 360d, 600d })
        {
            var samples = CreateSamples();
            var surface = CreateSurface(width, samples);
            for (var selection = 0; selection < 2; selection++)
            {
                foreach (var sample in samples)
                {
                    sample.Combo.SelectedIndex = selection;
                }
                Layout(surface);
                foreach (var sample in samples)
                {
                    var expected = sample.Expected[selection];
                    var actual = DisplayText(sample.Combo);
                    receipts.Add(new CaseReceipt(sample.Name, width, selection, expected, actual));
                    if (actual != expected)
                    {
                        failures.Add($"{sample.Name} at {width:0} DIP, selection {selection}: expected '{expected}', got '{actual}'.");
                    }
                }
                Render(surface, Path.Combine(outputDirectory, $"combo-display-{width:0}-selection-{selection}.png"));
            }
        }

        var dialogReceipts = VerifyCreationDialog(outputDirectory, failures);

        // The removed selector is the original bug: WPF's DisplayMemberPath
        // implementation provides a generated ItemTemplateSelector, not an ItemTemplate.
        var regressionSource = new XElement(resourcesSource);
        regressionSource.Descendants(Presentation + "ContentPresenter").Attributes("ContentTemplateSelector")
            .Where(attribute => attribute.Value.Contains("ItemTemplateSelector", StringComparison.Ordinal)).Remove();
        app.Resources = (ResourceDictionary)XamlReader.Parse(regressionSource.ToString());
        var regressionSample = CreateSamples()[0];
        var regressionSurface = CreateSurface(600, [regressionSample]);
        regressionSample.Combo.SelectedIndex = 0;
        Layout(regressionSurface);
        var regressionText = DisplayText(regressionSample.Combo);
        Render(regressionSurface, Path.Combine(outputDirectory, "combo-display-missing-selector-regression.png"));
        if (!regressionText.Contains("Option {", StringComparison.Ordinal)
            || regressionText == regressionSample.Expected[0])
        {
            failures.Add("The missing-template-selector negative control did not reproduce record ToString display.");
        }

        var receiptPath = Path.Combine(outputDirectory, "combo-display.json");
        File.WriteAllText(receiptPath, JsonSerializer.Serialize(new
        {
            Source = sourcePath,
            Scope = "Detached WPF ComboBoxes using actual App.xaml resources; no application windows or user sessions.",
            Cases = receipts,
            DialogCases = dialogReceipts,
            MissingSelectorRegression = regressionText,
            Failures = failures
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"COMBO_DISPLAY_RECEIPT {receiptPath}");
        if (failures.Count > 0)
        {
            throw new InvalidOperationException("ComboBox display checks failed: " + string.Join(Environment.NewLine, failures));
        }
        Console.WriteLine($"COMBO_DISPLAY_OK cases={receipts.Count} dialog-cases={dialogReceipts.Count} "
            + "renders=11 missing-selector-regression=reproduced");
    }

    private static List<DialogCaseReceipt> VerifyCreationDialog(string outputDirectory, List<string> failures)
    {
        var receipts = new List<DialogCaseReceipt>();
        foreach (var width in new[] { 560d, 640d })
        {
            var dialog = new HyperpagePageCreationDialog(
                [new VisualPageTemplateDocument { Name = "Landing starter", Scope = "page" }],
                [HyperpagePageFileType.Html, HyperpagePageFileType.RazorPage]);
            try
            {
                var body = (FrameworkElement)dialog.Content;
                dialog.Content = null;
                // Keep the real compiled dialog controls, bindings, and callbacks,
                // but render outside Window so neither Show nor a native HWND is needed.
                body.DataContext = dialog;
                var host = new ContentControl
                {
                    Content = body,
                    Width = width,
                    Height = dialog.Height,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch,
                    FontFamily = dialog.FontFamily,
                    FontSize = dialog.FontSize,
                    FontWeight = dialog.FontWeight,
                    Foreground = dialog.Foreground,
                    Background = (Brush)Application.Current.FindResource("AppBackgroundBrush"),
                    UseLayoutRounding = true
                };
                var fileTypes = Named<ComboBox>(dialog, "FileTypeComboBox");
                var templates = Named<ComboBox>(dialog, "TemplateComboBox");
                var name = Named<TextBox>(dialog, "PageNameTextBox");
                var description = Named<TextBox>(dialog, "DescriptionTextBox");
                var status = Named<TextBlock>(dialog, "StatusText");
                var link = Named<CheckBox>(dialog, "LinkStylesheetCheckBox");
                Named<TextBlock>(dialog, "StylesheetLocationText").Text = "Linked stylesheets are created in "
                    + "src/Pages/wwwroot/styles. Uncheck to create an unlinked document in the selected project folder.";
                var create = Descendants<Button>(body).Single(button => Equals(button.Content, "Create"));
                templates.SelectedIndex = 1;
                Layout(host);
                CaptureDialog("page", dialog, host, false, outputDirectory, failures, receipts);

                create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!status.Text.Contains("Describe", StringComparison.Ordinal) || dialog.CreationRequest is not null)
                {
                    failures.Add($"Page dialog at {width:0} DIP no longer requires a page description.");
                }

                fileTypes.SelectedItem = dialog.FileTypeOptions.Single(option => option.IsStylesheet);
                host.Height = dialog.Height;
                Layout(host);
                CaptureDialog("stylesheet", dialog, host, true, outputDirectory, failures, receipts);
                if (name.Text != "site.css" || description.Text.Length != 0 || !dialog.LinkStylesheetToCurrentPage)
                {
                    failures.Add($"Stylesheet dialog at {width:0} DIP did not preserve its prompt-free CSS defaults.");
                }
                link.IsChecked = false;
                if (dialog.LinkStylesheetToCurrentPage)
                {
                    failures.Add("The stylesheet linking checkbox does not update the creation choice.");
                }
                link.IsChecked = true;

                fileTypes.SelectedItem = dialog.FileTypeOptions.Single(option => option.FileType == HyperpagePageFileType.RazorPage);
                host.Height = dialog.Height;
                Layout(host);
                CaptureDialog("page-restored", dialog, host, false, outputDirectory, failures, receipts);
                if (name.Text != "New Page" || templates.SelectedIndex != 1)
                {
                    failures.Add("Switching from CSS back to a page did not restore the page default and selected template.");
                }

                fileTypes.SelectedItem = dialog.FileTypeOptions.Single(option => option.IsStylesheet);
                // A successful detached click reaches Window.DialogResult, which is
                // forbidden without ShowDialog. Accept only that exact final setter:
                // the real validation must already have produced the CSS result.
                try
                {
                    create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (InvalidOperationException exception) when (
                    exception.TargetSite?.Name == "set_DialogResult" && dialog.StylesheetFileName == "site.css")
                {
                }
                if (dialog.StylesheetFileName != "site.css" || dialog.CreationRequest is not null || status.Text.Length != 0)
                {
                    failures.Add($"Stylesheet creation at {width:0} DIP did not accept an empty AI description.");
                }
            }
            finally
            {
                dialog.Close();
            }
        }

        var directCss = new HyperpagePageCreationDialog([], [HyperpagePageFileType.Html], stylesheet: true);
        try
        {
            var directBody = (FrameworkElement)directCss.Content;
            directCss.Content = null;
            directBody.DataContext = directCss;
            var directHost = new ContentControl
            {
                Content = directBody,
                Width = 640,
                Height = directCss.Height,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };
            // Constructor selection is deferred until the inherited DataContext
            // populates ItemsSource. Match the other cases' first layout pass.
            Layout(directHost);
            if (Named<ComboBox>(directCss, "FileTypeComboBox").SelectedItem is not HyperpagePageCreationDialog.FileTypeOption { IsStylesheet: true }
                || Named<FrameworkElement>(directCss, "PageDescriptionSection").Visibility != Visibility.Collapsed)
            {
                failures.Add("Direct stylesheet entry did not start in prompt-free CSS mode.");
            }
        }
        finally
        {
            directCss.Close();
        }
        return receipts;
    }

    private static void CaptureDialog(string scenario, HyperpagePageCreationDialog dialog, FrameworkElement host,
        bool stylesheet, string outputDirectory, List<string> failures, List<DialogCaseReceipt> receipts)
    {
        var template = Named<ComboBox>(dialog, "TemplateComboBox");
        var fileType = Named<ComboBox>(dialog, "FileTypeComboBox");
        var pageSection = Named<FrameworkElement>(dialog, "PageDescriptionSection");
        var cssSection = Named<FrameworkElement>(dialog, "StylesheetSection");
        var templateSection = Named<FrameworkElement>(dialog, "TemplateSection");
        var link = Named<CheckBox>(dialog, "LinkStylesheetCheckBox");
        var expectedFileType = ((HyperpagePageCreationDialog.FileTypeOption)fileType.SelectedItem).Name;
        var templateLabel = DisplayText(template);
        var fileTypeLabel = DisplayText(fileType);
        var pageVisible = pageSection.Visibility == Visibility.Visible;
        var cssVisible = cssSection.Visibility == Visibility.Visible;
        if (templateLabel != "Landing starter" || fileTypeLabel != expectedFileType)
        {
            failures.Add($"{scenario} dialog at {host.Width:0} DIP displays object text instead of option names.");
        }
        if (pageVisible == stylesheet || cssVisible != stylesheet || templateSection.IsEnabled == stylesheet)
        {
            failures.Add($"{scenario} dialog at {host.Width:0} DIP has incorrect page/CSS field visibility or enabled state.");
        }
        if (stylesheet && (link.Visibility != Visibility.Visible || link.ActualHeight <= 0))
        {
            failures.Add("The CSS page-linking option is not visible in the stylesheet dialog.");
        }
        receipts.Add(new DialogCaseReceipt(scenario, host.Width, host.Height, templateLabel, fileTypeLabel,
            pageVisible, cssVisible, templateSection.IsEnabled, link.IsChecked == true));
        Render(host, Path.Combine(outputDirectory, $"creation-dialog-{scenario}-{host.Width:0}.png"));
    }

    private static T Named<T>(HyperpagePageCreationDialog dialog, string name) where T : FrameworkElement =>
        dialog.FindName(name) as T ?? throw new InvalidOperationException($"Dialog control {name} was not found.");

    private static Sample[] CreateSamples()
    {
        var options = new[] { new Option("Blank page", "razor"), new Option("Stylesheet (.css)", "css") };
        return
        [
            new("DisplayMemberPath · record", new ComboBox { ItemsSource = options, DisplayMemberPath = "Name" },
                ["Blank page", "Stylesheet (.css)"]),
            new("Nested DisplayMemberPath", new ComboBox
                { ItemsSource = options.Select(option => new { Details = option }).ToArray(), DisplayMemberPath = "Details.Name" },
                ["Blank page", "Stylesheet (.css)"]),
            new("Explicit ItemTemplate", new ComboBox
                { ItemsSource = options, ItemTemplate = TextTemplate("{Binding Code}") }, ["razor", "css"]),
            new("Explicit ItemTemplateSelector", new ComboBox
                { ItemsSource = options, ItemTemplateSelector = new LabelSelector() }, ["Selected: Blank page", "Selected: Stylesheet (.css)"]),
            new("Plain strings", new ComboBox { ItemsSource = new[] { "HTML", "CSS" } }, ["HTML", "CSS"]),
            new("Record fallback · intentional", new ComboBox { ItemsSource = options },
                [options[0].ToString(), options[1].ToString()]),
            new("ItemStringFormat", new ComboBox
                { ItemsSource = new[] { "HTML", "CSS" }, ItemStringFormat = "Language: {0}" }, ["Language: HTML", "Language: CSS"]),
            new("ComboBoxItem content", new ComboBox
                { ItemsSource = new[] { new ComboBoxItem { Content = "Base" }, new ComboBoxItem { Content = "Mobile" } } },
                ["Base", "Mobile"])
        ];
    }

    private static DataTemplate TextTemplate(string binding) => (DataTemplate)XamlReader.Parse(
        $"<DataTemplate xmlns='{Presentation}'><TextBlock Text=\"{binding}\" /></DataTemplate>");

    private static Grid CreateSurface(double width, Sample[] samples)
    {
        var surface = new Grid
        {
            Width = width,
            Height = samples.Length * 76 + 24,
            Background = (Brush)Application.Current.FindResource("AppBackgroundBrush"),
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };
        var rows = new StackPanel { Margin = new Thickness(20, 12, 20, 12) };
        surface.Children.Add(rows);
        foreach (var sample in samples)
        {
            rows.Children.Add(new TextBlock
            {
                Text = sample.Name,
                FontSize = 11,
                Foreground = (Brush)Application.Current.FindResource("MutedForegroundBrush"),
                Margin = new Thickness(2, 0, 0, 6)
            });
            sample.Combo.Height = 36;
            sample.Combo.FontSize = 13;
            sample.Combo.Margin = new Thickness(0, 0, 0, 16);
            rows.Children.Add(sample.Combo);
        }
        return surface;
    }

    private static void Layout(FrameworkElement surface)
    {
        surface.Measure(new Size(surface.Width, surface.Height));
        surface.Arrange(new Rect(0, 0, surface.Width, surface.Height));
        surface.UpdateLayout();
        surface.Dispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
        surface.UpdateLayout();
    }

    private static string DisplayText(ComboBox combo)
    {
        var presenter = Descendants<ContentPresenter>(combo).FirstOrDefault(element =>
            BindingOperations.GetBinding(element, ContentPresenter.ContentProperty)?.Path?.Path == "SelectionBoxItem")
            ?? throw new InvalidOperationException("ComboBox selected-item presenter did not materialize.");
        return string.Join("", Descendants<TextBlock>(presenter).Select(block => block.Text));
    }

    private static void Render(FrameworkElement surface, string path)
    {
        var image = new RenderTargetBitmap((int)surface.Width, (int)surface.Height, 96, 96, PixelFormats.Pbgra32);
        image.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static XElement ResourceSource(string path)
    {
        var root = XDocument.Load(path).Root ?? throw new InvalidOperationException("Missing App.xaml root.");
        var resources = new XElement(Presentation + "ResourceDictionary");
        foreach (var declaration in root.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
        {
            resources.Add(new XAttribute(declaration));
        }
        resources.Add(root.Element(Presentation + "Application.Resources")!.Elements().Select(element => new XElement(element)));
        foreach (var declaration in resources.DescendantsAndSelf().Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
        {
            if (declaration.Value.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal)
                && !declaration.Value.Contains(";assembly=", StringComparison.Ordinal))
            {
                declaration.Value += ";assembly=Hypershell";
            }
        }
        foreach (var element in resources.DescendantsAndSelf())
        {
            var ns = element.Name.NamespaceName;
            if (ns.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal) && !ns.Contains(";assembly=", StringComparison.Ordinal))
            {
                element.Name = XName.Get(element.Name.LocalName, ns + ";assembly=Hypershell");
            }
        }
        resources.Descendants().Attributes("Click").Remove();
        return resources;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }
            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static string FindAppSource()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var path = Path.Combine(directory.FullName, "src", "Hypershell", "App.xaml");
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }
        throw new FileNotFoundException("Cannot find App.xaml from the verifier working directory.");
    }

    public sealed record Option(string Name, string Code);
    private sealed record Sample(string Name, ComboBox Combo, string[] Expected);
    private sealed record CaseReceipt(string Scenario, double Width, int SelectedIndex, string Expected, string Actual);
    private sealed record DialogCaseReceipt(string Scenario, double Width, double Height, string TemplateLabel,
        string FileTypeLabel, bool PageDescriptionVisible, bool StylesheetOptionsVisible, bool TemplateEnabled, bool LinkChecked);
    private sealed class LabelSelector : DataTemplateSelector
    {
        private readonly DataTemplate _template = TextTemplate("{Binding Name, StringFormat='Selected: {0}'}");
        public override DataTemplate SelectTemplate(object item, DependencyObject container) => _template;
    }
}
