using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

public static class WorkspaceTabLayoutVerifier
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    public static void Verify(string outputDirectory, string? mainWindowPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        var sourcePath = mainWindowPath ?? FindMainWindow();
        Exception? failure = null;
        void Run()
        {
            try
            {
                VerifyOnSta(Path.GetFullPath(outputDirectory), Path.GetFullPath(sourcePath));
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
            var thread = new Thread(Run) { IsBackground = true, Name = "Workspace tab layout verifier" };
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
        if (Application.Current is null)
        {
            // A plain application has no StartupUri. Load the actual source resources
            // without the application's startup metadata or event-handler hookups.
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var appSource = XDocument.Load(Path.Combine(Path.GetDirectoryName(FindMainWindow())!, "App.xaml"));
            var appRoot = appSource.Root ?? throw new InvalidOperationException("Missing App.xaml root.");
            var resources = new XElement(Presentation + "ResourceDictionary");
            CopyNamespaces(appRoot, resources);
            resources.Add(appRoot.Element(Presentation + "Application.Resources")!.Elements()
                .Select(element => new XElement(element)));
            QualifyApplicationElements(resources);
            resources.Descendants().Attributes("Click").Remove();
            app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString());
        }
        else if (!Application.Current.Dispatcher.CheckAccess())
        {
            throw new InvalidOperationException("The existing WPF application belongs to another thread.");
        }

        var source = XDocument.Load(sourcePath);
        var failures = new List<string>();
        var cases = new List<CaseReceipt>();
        foreach (var width in new[] { 700d, 1440d })
        {
            var (surface, tabs) = CreateSurface(source, width);
            var samples = new[]
            {
                new TabSample("1", "#A85C2B"),
                new TabSample("nu · Home", "#7457A5"),
                new TabSample("A longer workspace title that should truncate gracefully", "#51958E")
            };
            tabs.ItemsSource = samples;
            tabs.SelectedIndex = 1;
            Layout(surface);
            CaptureCase("selected", surface, tabs, width, outputDirectory, failures, cases);

            samples[1].IsRenaming = true;
            Layout(surface);
            CaptureCase("rename", surface, tabs, width, outputDirectory, failures, cases);
            samples[1].IsRenaming = false;

            tabs.ItemsSource = Enumerable.Range(1, 16)
                .Select(index => new TabSample($"Workspace {index} · Example page", "#7457A5"))
                .ToArray();
            tabs.SelectedIndex = tabs.Items.Count - 1;
            Layout(surface);
            SelectedItem(tabs).BringIntoView();
            Layout(surface);
            CaptureCase("overflow-last", surface, tabs, width, outputDirectory, failures, cases);

            tabs.SelectedIndex = 0;
            Layout(surface);
            SelectedItem(tabs).BringIntoView();
            Layout(surface);
            CaptureCase("overflow-first", surface, tabs, width, outputDirectory, failures, cases);
        }

        var studioHeaderCases = new List<StudioHeaderCaseReceipt>();
        CaptureStudioHeaders(source, outputDirectory, failures, studioHeaderCases);

        var contentCases = new List<ContentCaseReceipt>();
        foreach (var (width, height) in new[] { (1100d, 700d), (1440d, 900d), (1800d, 1100d) })
        {
            CaptureStudioContent(source, width, height, outputDirectory, failures, contentCases);
        }

        CaptureAiBrand(source, outputDirectory, failures, Path.GetDirectoryName(sourcePath)!);

        // Keep a negative control for the regression. WPF copies the selected
        // TabItem's content alignment onto PART_SelectedContentHost, even when
        // the header itself appears correctly aligned.
        var centeredSource = new XDocument(source);
        var workspace = centeredSource.Descendants(Presentation + "TabControl").Single(element =>
            (string?)element.Attribute(Xaml + "Name") == "WorkspaceTabs");
        var itemStyle = workspace.Element(Presentation + "TabControl.ItemContainerStyle")!
            .Element(Presentation + "Style")!;
        foreach (var property in new[] { "HorizontalContentAlignment", "VerticalContentAlignment" })
        {
            itemStyle.Elements(Presentation + "Setter").Single(setter =>
                (string?)setter.Attribute("Property") == property).SetAttributeValue("Value", "Center");
        }
        var regressionFailures = new List<string>();
        var regressionCases = new List<ContentCaseReceipt>();
        CaptureStudioContent(centeredSource, 1440, 1100, outputDirectory,
            regressionFailures, regressionCases, "centered-regression");
        var regression = regressionCases.Single();
        if (regression.TopGap < 10 || regression.BottomGap < 10
            || Math.Abs(regression.TopGap - regression.BottomGap) > 1 || regressionFailures.Count == 0)
        {
            failures.Add("The centered-alignment negative control did not reproduce equal vertical content gaps.");
        }

        var receiptPath = Path.Combine(outputDirectory, "workspace-tab-layout.json");
        File.WriteAllText(receiptPath, JsonSerializer.Serialize(new
        {
            Source = sourcePath,
            Scope = "Real workspace tab XAML and application styles, detached from application sessions.",
            ScaleCoverage = "96/144/192-DPI bitmap rendering; this does not change the live Windows display DPI.",
            Cases = cases,
            StudioHeaderCases = studioHeaderCases,
            ContentCases = contentCases,
            AiBrandCoverage = "Canonical spiral shell; shared native glyph at 16/20/24/32/48 DIP; actual live-toggle template off/on/disabled with inherited tint and disabled opacity.",
            CenteredAlignmentRegression = new { Cases = regressionCases, ExpectedFailures = regressionFailures },
            Failures = failures
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"WORKSPACE_TAB_LAYOUT_RECEIPT {receiptPath}");
        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"Workspace tab layout failed {failures.Count} checks. "
                + string.Join(Environment.NewLine, failures.Take(12)));
        }

        Console.WriteLine($"WORKSPACE_TAB_LAYOUT_OK cases={cases.Count} renders={cases.Count * 3} "
            + $"studio-header-cases={studioHeaderCases.Count} studio-content-cases={contentCases.Count} "
            + $"centered-regression-gaps={regression.TopGap:0.##}/{regression.BottomGap:0.##}");
    }

    private static void CaptureStudioHeaders(XDocument source, string outputDirectory,
        List<string> failures, List<StudioHeaderCaseReceipt> cases)
    {
        var original = source.Descendants(Presentation + "DockPanel").Single(element =>
            (string?)element.Attribute(Xaml + "Name") == "StudioWorkspaceHeader");
        var originalAdd = original.Descendants(Presentation + "Button").Single(element =>
            (string?)element.Attribute("AutomationProperties.Name") == "New project tab");
        if ((string?)originalAdd.Attribute("Click") != "NewStudioProjectClick"
            || originalAdd.Descendants(Presentation + "MenuItem").Any())
        {
            failures.Add("The Studio plus must directly open a project start tab without obsolete object menus.");
        }

        foreach (var standalone in new[] { false, true })
        foreach (var width in new[] { 320d, 700d, 1440d })
        foreach (var scenario in new[] { "empty", "single", "many", "long" })
        {
            var markup = new XElement(original);
            markup.DescendantsAndSelf().Attributes("Click").Remove();
            var wrapper = new XElement(Presentation + "Grid");
            CopyNamespaces(source.Root!, wrapper);
            wrapper.Add(markup);
            QualifyApplicationElements(wrapper);
            var surface = (Grid)XamlReader.Parse(wrapper.ToString());
            surface.Width = width;
            surface.Height = 46;
            surface.Background = (Brush)Application.Current.FindResource("AppBackgroundBrush");
            surface.UseLayoutRounding = true;
            TextElement.SetFontFamily(surface, (FontFamily)Application.Current.FindResource("UiFontFamily"));
            var samples = scenario switch
            {
                "empty" => Array.Empty<StudioTabSample>(),
                "single" => [new StudioTabSample("Home", "Work")],
                "many" => Enumerable.Range(1, 12).Select(index =>
                    new StudioTabSample($"Chapter {index} · Climate", "Page")).ToArray(),
                _ => [new StudioTabSample("A very long chapter title that must stay bounded instead of pushing the add button outside the scrollable strip", "Page")]
            };
            surface.DataContext = new StudioHeaderSample(standalone, samples);
            Layout(surface);
            var tabs = Descendants<ListBox>(surface).Single(element => element.Name == "StudioObjectTabs");
            var add = Descendants<Button>(surface).Single(element =>
                System.Windows.Automation.AutomationProperties.GetName(element) == "New project tab");
            var viewer = Descendants<ScrollViewer>(surface).Single(element => element.Name == "StudioHeaderScrollViewer");
            var viewport = Descendants<ScrollContentPresenter>(viewer).First();
            var strip = (FrameworkElement)viewer.Content;
            var label = $"Studio {scenario}, {(standalone ? "standalone" : "Hypershell")}, {width:0} DIP";
            var last = samples.Length > 0
                ? (ListBoxItem)tabs.ItemContainerGenerator.ContainerFromIndex(samples.Length - 1)
                : null;
            var trailingEdge = last is null ? 0 : BoundsIn(last, strip).Right;
            var addGap = BoundsIn(add, strip).Left - trailingEdge;
            if (addGap < -0.5 || addGap > 8)
            {
                failures.Add($"{label}: plus is {addGap:0.##} DIP after the last tab; expected adjacent.");
            }
            if (Ancestor<ScrollViewer>(add) != viewer || !add.Focusable || !add.IsTabStop)
            {
                failures.Add($"{label}: plus must share the bounded tab strip and remain keyboard reachable.");
            }
            if (viewer.ActualWidth <= 0 || viewer.ActualWidth > width
                || BoundsIn(viewer, surface).Right > width + 0.5)
            {
                failures.Add($"{label}: Studio scroll viewport exceeded the available header width.");
            }
            if (last is not null)
            {
                tabs.SelectedIndex = samples.Length - 1;
                Layout(surface);
                last.BringIntoView();
                Layout(surface);
                var lastBounds = BoundsIn(last, viewport);
                if (last.ActualWidth <= viewport.ActualWidth
                    && (lastBounds.Left < -0.5 || lastBounds.Right > viewport.ActualWidth + 0.5))
                {
                    failures.Add($"{label}: the final selected tab could not be brought into view.");
                }
            }
            add.BringIntoView();
            Layout(surface);
            var addBounds = BoundsIn(add, viewport);
            CheckVertical(addBounds, viewport.ActualHeight, label + " plus", failures);
            if (addBounds.Left < -0.5 || addBounds.Right > viewport.ActualWidth + 0.5
                || viewer.ScrollableWidth > 0 && viewer.HorizontalOffset <= 0)
            {
                failures.Add($"{label}: the plus could not be brought fully into the scroll viewport.");
            }
            cases.Add(new StudioHeaderCaseReceipt(scenario, standalone, width, samples.Length,
                viewer.ViewportWidth, viewer.ExtentWidth, viewer.HorizontalOffset, addGap, Rectangle(addBounds)));
            var image = new RenderTargetBitmap((int)width, (int)surface.Height, 96, 96, PixelFormats.Pbgra32);
            image.Render(surface);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(Path.Combine(outputDirectory,
                $"studio-tabs-{(standalone ? "standalone" : "hypershell")}-{width:0}-{scenario}.png")))
            {
                encoder.Save(stream);
            }
            if (samples.Length > 0)
            {
                tabs.SelectedIndex = 0;
                Layout(surface);
                var first = (ListBoxItem)tabs.ItemContainerGenerator.ContainerFromIndex(0);
                first.BringIntoView();
                Layout(surface);
                if (first.ActualWidth <= viewport.ActualWidth && BoundsIn(first, viewport).Left < -0.5)
                {
                    failures.Add($"{label}: the first tab could not be brought back into view.");
                }
            }
        }
    }

    private static void CaptureAiBrand(XDocument source, string outputDirectory, List<string> failures, string applicationSourceDirectory)
    {
        var dictionaryMarkup = new XElement(Presentation + "ResourceDictionary");
        CopyNamespaces(source.Root!, dictionaryMarkup);
        dictionaryMarkup.Add(new XElement(source.Descendants(Presentation + "Style").Single(element =>
            (string?)element.Attribute(Xaml + "Key") == "AiLiveToggleStyle")));
        QualifyApplicationElements(dictionaryMarkup);
        var resources = (ResourceDictionary)XamlReader.Parse(dictionaryMarkup.ToString());
        var style = (Style)resources["AiLiveToggleStyle"];
        var surface = new Grid
        {
            Width = 600, Height = 220,
            Background = (Brush)Application.Current.FindResource("AppBackgroundBrush"),
            UseLayoutRounding = true
        };
        var rows = new StackPanel { Margin = new Thickness(24) };
        surface.Children.Add(rows);
        var brandRow = new StackPanel { Orientation = Orientation.Horizontal };
        rows.Children.Add(brandRow);
        brandRow.Children.Add(new Image
        {
            Width = 72, Height = 72,
            Source = new BitmapImage(new Uri(Path.Combine(applicationSourceDirectory,
                "Assets", "Brand", "hypershell-spiral-shell.png"))),
            Margin = new Thickness(0, 0, 20, 0)
        });
        var labelBrush = (Brush)Application.Current.FindResource("ForegroundBrush");
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock { Text = "Hypershell", FontSize = 21, Foreground = labelBrush });
        labels.Children.Add(new TextBlock
        {
            Text = "Spiral shell · Native glyph states", FontSize = 12,
            Foreground = (Brush)Application.Current.FindResource("MutedForegroundBrush"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        brandRow.Children.Add(labels);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 26, 0, 0) };
        rows.Children.Add(controls);
        var toggles = new List<ToggleButton>();
        foreach (var (label, active, enabled) in new[] { ("Idle", false, true), ("Live", true, true), ("Disabled", false, false) })
        {
            var cell = new StackPanel { Width = 80 };
            var toggle = new ToggleButton { Style = style, IsChecked = active, IsEnabled = enabled };
            toggles.Add(toggle);
            cell.Children.Add(toggle);
            cell.Children.Add(new TextBlock { Text = label, Foreground = labelBrush, FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
            controls.Children.Add(cell);
        }
        var standaloneGlyphs = new List<Hypershell.Controls.AiShellGlyph>();
        foreach (var size in new[] { 16d, 20d, 24d, 32d, 48d })
        {
            var glyph = new Hypershell.Controls.AiShellGlyph
            {
                Width = size, Height = size, Foreground = labelBrush,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(24, 0, 0, 0)
            };
            standaloneGlyphs.Add(glyph);
            controls.Children.Add(glyph);
        }
        Layout(surface);
        foreach (var glyph in standaloneGlyphs)
        {
            if (glyph.ActualWidth > glyph.Width + 0.1 || glyph.ActualHeight > glyph.Height + 0.1
                || LayoutInformation.GetLayoutClip(glyph) is { } clip
                    && !clip.Bounds.Contains(new Rect(0, 0, glyph.ActualWidth, glyph.ActualHeight)))
            {
                failures.Add($"The AI shell glyph is clipped at {glyph.Width:0} DIP.");
            }
        }
        foreach (var toggle in toggles)
        {
            var glyph = (FrameworkElement)toggle.Template.FindName("ShellCore", toggle);
            var bounds = BoundsIn(glyph, toggle);
            if (bounds.Left < 0 || bounds.Top < 0 || bounds.Right > toggle.ActualWidth || bounds.Bottom > toggle.ActualHeight)
            {
                failures.Add("The shared AI shell glyph exceeds its live-toggle surface.");
            }
            if (TextElement.GetForeground(glyph).ToString() != toggle.Foreground.ToString())
            {
                failures.Add("The shared AI shell glyph does not inherit its live-toggle tint.");
            }
        }
        if (toggles[0].Foreground.ToString() == toggles[1].Foreground.ToString())
        {
            failures.Add("The AI live and idle states must have distinct foregrounds.");
        }
        var idleSurface = (FrameworkElement)toggles[0].Template.FindName("LiveToggleSurface", toggles[0]);
        var disabledSurface = (FrameworkElement)toggles[2].Template.FindName("LiveToggleSurface", toggles[2]);
        if (disabledSurface.Opacity >= idleSurface.Opacity)
        {
            failures.Add("The disabled AI toggle must remain visibly subdued.");
        }
        foreach (var scale in new[] { 1d, 1.5d, 2d })
        {
            var bitmap = new RenderTargetBitmap((int)(surface.Width * scale), (int)(surface.Height * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(surface);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(outputDirectory, $"ai-brand-{scale * 100:0}.png"));
            encoder.Save(stream);
        }
    }

    private static (Grid Surface, TabControl Tabs) CreateSurface(XDocument source, double width, double height = 130)
    {
        var window = source.Root ?? throw new InvalidOperationException("Missing MainWindow root.");
        var markup = new XElement(window.Descendants(Presentation + "TabControl").Single(element =>
            (string?)element.Attribute(Xaml + "Name") == "WorkspaceTabs"));
        markup.Attribute("ItemsSource")?.Remove();
        markup.Attribute("SelectedItem")?.Remove();
        markup.Elements(Presentation + "TabControl.Resources").Remove();

        // The header, container template, scrolling and selected-content bindings
        // remain intact. Only session data and interactive callbacks are replaced.
        markup.Descendants().Where(element =>
            element.Name.LocalName.EndsWith(".ContextMenu", StringComparison.Ordinal)).ToList().ForEach(element => element.Remove());
        var eventNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Click", "SelectionChanged", "ContextMenuOpening", "MouseLeftButtonDown",
            "PreviewKeyDown", "LostKeyboardFocus", "Loaded", "Unloaded", "DataContextChanged"
        };
        foreach (var attribute in markup.DescendantsAndSelf().Attributes()
                     .Where(attribute => eventNames.Contains(attribute.Name.LocalName)).ToList())
        {
            attribute.Remove();
        }

        var wrapper = new XElement(Presentation + "Grid");
        CopyNamespaces(window, wrapper);
        wrapper.Add(markup);
        QualifyApplicationElements(wrapper);
        var surface = (Grid)XamlReader.Parse(wrapper.ToString());
        surface.Width = width;
        surface.Height = height;
        surface.Background = (Brush)Application.Current.FindResource("AppBackgroundBrush");
        surface.UseLayoutRounding = true;
        surface.SnapsToDevicePixels = true;
        TextElement.SetFontFamily(surface, (FontFamily)Application.Current.FindResource("UiFontFamily"));
        TextElement.SetFontSize(surface, (double?)window.Attribute("FontSize") ?? 13);
        TextElement.SetFontWeight(surface, FontWeights.Medium);
        surface.DataContext = new CaptionSample();
        var tabs = (TabControl)surface.Children[0];
        tabs.ContentTemplate = (DataTemplate)XamlReader.Parse(
            $"<DataTemplate xmlns=\"{Presentation}\"><ContentPresenter Content=\"{{Binding Body}}\" /></DataTemplate>");
        return (surface, tabs);
    }

    private static UserControl CreateStudioContent()
    {
        // Read the actual Studio markup into a plain UserControl. Avoid constructing
        // VisualPageStudioView: its constructor requires a live session/view model
        // and builds WebView2 panes. This preserves its real layout and styles.
        var source = XDocument.Load(Path.Combine(Path.GetDirectoryName(FindMainWindow())!, "VisualPageStudioView.xaml"));
        var root = new XElement(source.Root!);
        root.Attribute(Xaml + "Class")?.Remove();
        var eventNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Click", "SelectionChanged", "SelectedItemChanged", "MouseDoubleClick",
            "PreviewKeyDown", "IsVisibleChanged"
        };
        root.DescendantsAndSelf().Attributes()
            .Where(attribute => eventNames.Contains(attribute.Name.LocalName)).Remove();
        QualifyApplicationElements(root);
        var view = (UserControl)XamlReader.Parse(root.ToString());
        view.DataContext = new StudioSample();
        var leftTools = (TabControl)view.FindName("LeftToolTabs");
        leftTools.SelectedItem = leftTools.Items.OfType<TabItem>().Single(item => Equals(item.Header, "Elements"));
        var rightTools = (TabControl)view.FindName("RightToolTabs");
        rightTools.SelectedItem = rightTools.Items.OfType<TabItem>().Single(item => Equals(item.Header, "Properties"));
        var pane = (ContentControl)view.FindName("PaneHost");
        pane.Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(11, 16, 21)),
            BorderBrush = (Brush)Application.Current.FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(10, 46, 10, 10),
            Child = new TextBlock
            {
                Text = "Canvas · Layout verification",
                Foreground = (Brush)Application.Current.FindResource("MutedForegroundBrush"),
                Margin = new Thickness(18),
                VerticalAlignment = VerticalAlignment.Top
            }
        };
        return view;
    }

    private static void CaptureStudioContent(
        XDocument source, double width, double height, string outputDirectory,
        List<string> failures, List<ContentCaseReceipt> cases, string scenario = "studio-fill")
    {
        var (surface, tabs) = CreateSurface(source, width, height);
        var studio = CreateStudioContent();
        tabs.ItemsSource = new[]
        {
            new TabSample("1", "#A85C2B"),
            new TabSample("nu · Home", "#7457A5") { Body = studio }
        };
        tabs.SelectedIndex = 1;
        Layout(surface);
        var host = (ContentPresenter)tabs.Template.FindName("PART_SelectedContentHost", tabs);
        var area = (FrameworkElement)VisualTreeHelper.GetParent(host);
        var areaBounds = BoundsIn(area, surface);
        var hostBounds = BoundsIn(host, surface);
        var studioBounds = BoundsIn(studio, surface);
        var panelBounds = BoundsIn((FrameworkElement)studio.FindName("StudioPanelGrid"), surface);
        var leftBounds = BoundsIn((FrameworkElement)studio.FindName("LeftToolPanelHost"), surface);
        var rightBounds = BoundsIn((FrameworkElement)studio.FindName("RightToolPanelHost"), surface);
        var centerBounds = BoundsIn((FrameworkElement)studio.FindName("CentralWorkspaceHost"), surface);
        var label = $"{scenario} at {width:0}x{height:0} DIP";
        foreach (var (name, bounds) in new[]
                 {
                     ("selected content host", hostBounds), ("Studio", studioBounds), ("Studio panels", panelBounds)
                 })
        {
            if (Math.Abs(bounds.Top - areaBounds.Top) > 0.5 || Math.Abs(bounds.Bottom - areaBounds.Bottom) > 0.5
                || Math.Abs(bounds.Left - areaBounds.Left) > 0.5 || Math.Abs(bounds.Right - areaBounds.Right) > 0.5)
            {
                failures.Add($"{label}: {name} bounds {bounds} do not fill workspace area {areaBounds}.");
            }
        }
        foreach (var (name, bounds) in new[] { ("left panel", leftBounds), ("right panel", rightBounds) })
        {
            if (bounds.Width <= 0 || Math.Abs(bounds.Top - areaBounds.Top) > 0.5
                || Math.Abs(bounds.Bottom - areaBounds.Bottom) > 0.5)
            {
                failures.Add($"{label}: {name} does not span the available workspace height.");
            }
        }
        if (Math.Abs(centerBounds.Bottom - areaBounds.Bottom) > 0.5
            || Math.Abs(centerBounds.Top - areaBounds.Top - 50) > 0.5)
        {
            failures.Add($"{label}: canvas/assistant region must fill from the 50-DIP Studio toolbar to the workspace bottom.");
        }
        cases.Add(new ContentCaseReceipt(scenario, width, height,
            Rectangle(areaBounds), Rectangle(hostBounds), Rectangle(studioBounds),
            Rectangle(leftBounds), Rectangle(rightBounds), Rectangle(centerBounds),
            studioBounds.Top - areaBounds.Top, areaBounds.Bottom - studioBounds.Bottom));
        var image = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        image.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(outputDirectory, $"{scenario}-{width:0}x{height:0}.png"));
        encoder.Save(stream);
    }

    private static void CopyNamespaces(XElement source, XElement target)
    {
        foreach (var attribute in source.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
        {
            var value = attribute.Value;
            if (value.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal)
                && !value.Contains(";assembly=", StringComparison.Ordinal))
            {
                value += ";assembly=Hypershell";
            }
            target.Add(new XAttribute(attribute.Name, value));
        }
    }

    private static void QualifyApplicationElements(XElement root)
    {
        foreach (var declaration in root.DescendantsAndSelf().Attributes()
                     .Where(attribute => attribute.IsNamespaceDeclaration))
        {
            if (declaration.Value.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal)
                && !declaration.Value.Contains(";assembly=", StringComparison.Ordinal))
            {
                declaration.Value += ";assembly=Hypershell";
            }
        }
        foreach (var element in root.DescendantsAndSelf())
        {
            var name = element.Name.NamespaceName;
            if (name.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal)
                && !name.Contains(";assembly=", StringComparison.Ordinal))
            {
                element.Name = XName.Get(element.Name.LocalName, name + ";assembly=Hypershell");
            }
        }
    }

    private static void CaptureCase(
        string scenario,
        Grid surface,
        TabControl tabs,
        double width,
        string outputDirectory,
        List<string> failures,
        List<CaseReceipt> cases)
    {
        var headerPanel = (TabPanel?)tabs.Template.FindName("HeaderPanel", tabs)
            ?? throw new InvalidOperationException("Workspace HeaderPanel did not materialize.");
        var viewer = Ancestor<ScrollViewer>(headerPanel)
            ?? throw new InvalidOperationException("Workspace headers need a ScrollViewer.");
        var viewport = Descendants<ScrollContentPresenter>(viewer).FirstOrDefault()
            ?? throw new InvalidOperationException("The header scroll viewport did not materialize.");
        var label = $"{scenario} at {width:0} DIP";
        var geometry = new List<TabReceipt>();
        var selectedSurface = default(Color?);
        var inactiveSurface = default(Color?);
        var stripSurface = ((SolidColorBrush)surface.Background).Color;

        for (var index = 0; index < tabs.Items.Count; index++)
        {
            var item = (TabItem?)tabs.ItemContainerGenerator.ContainerFromIndex(index)
                ?? throw new InvalidOperationException($"Tab {index} did not materialize.");
            var title = Descendants<TextBlock>(item).Single(element => element.Name == "TabTitleText");
            var editor = Descendants<TextBox>(item).Single(element => element.Name == "TabTitleEditor");
            var header = (FrameworkElement)VisualTreeHelper.GetParent(title);
            var close = Descendants<Button>(header).Single(element => Equals(element.ToolTip, "Close tab"));
            var tabBorder = (Border?)item.Template.FindName("TabBorder", item)
                ?? throw new InvalidOperationException("TabBorder did not materialize.");
            var itemBounds = BoundsIn(item, viewport);
            var borderBounds = BoundsIn(tabBorder, viewport);
            var headerBounds = BoundsIn(header, viewport);
            var closeBounds = BoundsIn(close, viewport);
            var content = editor.Visibility == Visibility.Visible ? (FrameworkElement)editor : title;
            var contentBounds = BoundsIn(content, viewport);
            CheckVertical(headerBounds, viewport.ActualHeight, $"{label}, tab {index} header", failures);
            CheckVertical(closeBounds, viewport.ActualHeight, $"{label}, tab {index} close button", failures);
            CheckVertical(contentBounds, viewport.ActualHeight, $"{label}, tab {index} label/editor", failures);
            var contentWithinBorder = BoundsIn(header, tabBorder);
            CheckVertical(contentWithinBorder, tabBorder.ActualHeight,
                $"{label}, tab {index} header within tab surface", failures);
            CheckLayoutClips(tabBorder, viewport, $"{label}, tab {index} painted surface", failures);
            CheckLayoutClips(header, viewport, $"{label}, tab {index} header", failures);
            CheckLayoutClips(close, viewport, $"{label}, tab {index} close button", failures);
            CheckLayoutClips(content, viewport, $"{label}, tab {index} label/editor", failures);
            var itemClip = LayoutInformation.GetLayoutClip(item);
            if (item.IsSelected)
            {
                selectedSurface = (tabBorder.Background as SolidColorBrush)?.Color;
                if (scenario.StartsWith("overflow-", StringComparison.Ordinal)
                    && (itemBounds.Left < -1 || itemBounds.Right > viewport.ActualWidth + 1))
                {
                    failures.Add($"{label}: selected tab is outside the horizontal viewport ({itemBounds}).");
                }
            }
            else
            {
                inactiveSurface = (tabBorder.Background as SolidColorBrush)?.Color;
            }
            geometry.Add(new TabReceipt(index, item.IsSelected, editor.Visibility == Visibility.Visible,
                Rectangle(itemBounds), Rectangle(borderBounds),
                itemClip is null ? null : Rectangle(itemClip.Bounds),
                Rectangle(headerBounds), Rectangle(closeBounds), Rectangle(contentBounds)));
        }

        if (selectedSurface is null || inactiveSurface is null
            || selectedSurface == inactiveSurface || inactiveSurface.Value.A == 0
            || inactiveSurface == stripSurface)
        {
            failures.Add($"{label}: selected/inactive tabs need distinct visible surfaces above the strip background.");
        }
        if (scenario == "rename" && !geometry.Any(item => item.IsSelected && item.IsRenaming))
        {
            failures.Add($"{label}: selecting rename did not show the inline editor.");
        }
        if (scenario == "overflow-last" && viewer.HorizontalOffset <= 0)
        {
            failures.Add($"{label}: BringIntoView did not scroll to the last tab.");
        }
        // BringIntoView may scroll away the first item's leading margin. The selected
        // item's actual bounds above establish visibility without requiring offset zero.

        var addButton = Descendants<Button>(viewer).Single(button =>
            System.Windows.Automation.AutomationProperties.GetName(button) == "New tab");
        CheckVertical(BoundsIn(addButton, viewport), viewport.ActualHeight, $"{label}, new-tab button", failures);
        cases.Add(new CaseReceipt(scenario, width, viewport.ActualWidth, viewport.ActualHeight,
            viewer.HorizontalOffset, selectedSurface?.ToString(), inactiveSurface?.ToString(), geometry));
        foreach (var scale in new[] { 1d, 1.5d, 2d })
        {
            var image = new RenderTargetBitmap((int)Math.Ceiling(width * scale),
                (int)Math.Ceiling(surface.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            image.Render(surface);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(Path.Combine(outputDirectory,
                $"tabs-{width:0}-{scale * 100:0}-{scenario}.png"));
            encoder.Save(stream);
        }
    }

    private static void Layout(FrameworkElement surface)
    {
        surface.Measure(new Size(surface.Width, surface.Height));
        surface.Arrange(new Rect(0, 0, surface.Width, surface.Height));
        surface.UpdateLayout();
        surface.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        surface.UpdateLayout();
    }

    private static TabItem SelectedItem(TabControl tabs) =>
        (TabItem)(tabs.ItemContainerGenerator.ContainerFromIndex(tabs.SelectedIndex)
            ?? throw new InvalidOperationException("Selected tab did not materialize."));

    private static Rect BoundsIn(FrameworkElement element, Visual ancestor) =>
        element.TransformToVisual(ancestor).TransformBounds(new Rect(element.RenderSize));

    private static void CheckVertical(Rect rectangle, double viewportHeight, string name, List<string> failures)
    {
        if (rectangle.Height <= 0 || rectangle.Top < -0.5 || rectangle.Bottom > viewportHeight + 0.5)
        {
            failures.Add($"{name}: vertical bounds {rectangle.Top:0.##}..{rectangle.Bottom:0.##} "
                + $"are outside 0..{viewportHeight:0.##} DIP.");
        }
    }

    private static void CheckLayoutClips(
        FrameworkElement element, ScrollContentPresenter viewport, string name, List<string> failures)
    {
        // RenderSize alone can exceed a TabItem's arranged slot. In particular,
        // TabPanel can produce a clip smaller than the tab when margins are external.
        // Border.RenderSize excludes Margin, so this also protects its painted corners.
        for (DependencyObject? current = element; current is not null && current != viewport;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is not FrameworkElement frameworkElement)
            {
                continue;
            }
            var clip = LayoutInformation.GetLayoutClip(frameworkElement);
            if (clip is null)
            {
                continue;
            }
            var paintedBounds = BoundsIn(element, frameworkElement);
            paintedBounds.Inflate(-0.05, -0.05);
            if (clip.FillContainsWithDetail(new RectangleGeometry(paintedBounds)) != IntersectionDetail.FullyContains)
            {
                failures.Add($"{name}: painted bounds {paintedBounds} exceed "
                    + $"{frameworkElement.GetType().Name} layout clip {clip.Bounds}.");
            }
        }
    }

    private static BoundsReceipt Rectangle(Rect rectangle) =>
        new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);

    private static T? Ancestor<T>(DependencyObject element) where T : DependencyObject
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null;
             parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is T found)
            {
                return found;
            }
        }
        return null;
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

    private static string FindMainWindow()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var path = Path.Combine(directory.FullName, "src", "Hypershell", "MainWindow.xaml");
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }
        throw new FileNotFoundException("Cannot find MainWindow.xaml from the verifier working directory.");
    }

    public sealed class CaptionSample
    {
        public string AutosaveStatusText => "Saved";
        public bool ShowSaveStatusInTitleBar => false;
        public string ProfileSyncTitleToolTip => "Layout verification";
        public string ProfileAvatarInitials => "HS";
        public bool HasProfileAvatarImage => false;
        public ImageSource? ProfileAvatarImageSource => null;
    }

    public sealed class TabSample(string title, string color) : INotifyPropertyChanged
    {
        private bool _isRenaming;
        public FrameworkElement? Body { get; init; }
        public string DisplayTitle { get; } = title;
        public string RenameText { get; set; } = title;
        public Brush TabBackgroundBrush { get; } = (Brush)new BrushConverter().ConvertFromString(color)!;
        public Brush TabForegroundBrush { get; } = (Brush)Application.Current.FindResource("ForegroundBrush");
        public bool IsRenaming
        {
            get => _isRenaming;
            set
            {
                _isRenaming = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRenaming)));
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public sealed class StudioSample
    {
        public bool IsLeftToolPanelOpen => true;
        public bool IsRightToolPanelOpen => true;
        public bool IsAiAssistantOpen => true;
        public GridLength LeftToolPanelWidth => new(250);
        public GridLength RightToolPanelWidth => new(280);
        public GridLength LeftToolPanelSplitterWidth => new(6);
        public GridLength RightToolPanelSplitterWidth => new(6);
        public GridLength AiAssistantSplitterHeight => new(6);
        public GridLength AiAssistantPanelHeight => new(260);
        public bool IsCanvasOnlyWorkspace => true;
        public string[] ProjectPages { get; } = ["Home"];
        public string SelectedProjectPage { get; set; } = "Home";
    }

    public sealed class StudioHeaderSample(bool standalone, StudioTabSample[] tabs)
    {
        public bool IsVisualPageStudioActive => true;
        public bool IsHypershellWorkspace => !standalone;
        public bool IsStandaloneStudio => standalone;
        public bool IsHyperlibStudioActive => false;
        public StudioTabSample[] StudioWorkspaceTabs => tabs;
        public StudioTabSample? ActiveStudioWorkspaceTab { get; set; } = tabs.FirstOrDefault();
    }

    public sealed class StudioTabSample(string title, string kind)
    {
        public string StudioProjectKindLabel => kind;
        public string DisplayTitle => title;
        public string StudioDisplayTitle => title;
        public string StudioObjectKind => kind;
        public bool HasUnsavedChanges => false;
    }

    private sealed record BoundsReceipt(double X, double Y, double Width, double Height);
    private sealed record TabReceipt(int Index, bool IsSelected, bool IsRenaming,
        BoundsReceipt Tab, BoundsReceipt PaintedSurface, BoundsReceipt? ItemLayoutClip,
        BoundsReceipt Header, BoundsReceipt CloseButton, BoundsReceipt LabelOrEditor);
    private sealed record CaseReceipt(string Scenario, double Width, double ViewportWidth,
        double ViewportHeight, double HorizontalOffset, string? SelectedSurface, string? InactiveSurface,
        List<TabReceipt> Tabs);
    private sealed record ContentCaseReceipt(string Scenario, double Width, double Height,
        BoundsReceipt WorkspaceArea, BoundsReceipt SelectedContentHost, BoundsReceipt Studio,
        BoundsReceipt LeftPanel, BoundsReceipt RightPanel, BoundsReceipt CanvasAndAssistant,
        double TopGap, double BottomGap);
    private sealed record StudioHeaderCaseReceipt(string Scenario, bool Standalone, double Width,
        int Tabs, double ViewportWidth, double ExtentWidth, double HorizontalOffset,
        double AddGap, BoundsReceipt AddButton);
}
