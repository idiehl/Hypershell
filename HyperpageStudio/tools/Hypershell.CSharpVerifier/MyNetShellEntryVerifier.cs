using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

internal static class MyNetShellEntryVerifier
{
    private static readonly XNamespace P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static void Verify(string outputDirectory)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { VerifyCore(Path.GetFullPath(outputDirectory)); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true, Name = "MyNet panel entry layout verifier" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void VerifyCore(string outputDirectory)
    {
        var sourceDirectory = Path.Combine(Environment.CurrentDirectory, "src", "Hypershell");
        var mainSource = XDocument.Load(Path.Combine(sourceDirectory, "MainWindow.xaml"));
        var source = XDocument.Load(Path.Combine(sourceDirectory, "Controls", "AtlasMyNetView.xaml"));
        var entry = source.Descendants(P + "Button").Single(e => (string?)e.Attribute(X + "Name") == "MyNetDesktopButton");
        var checks = 0;
        void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(message);
        }
        Require(!mainSource.Descendants(P + "Button").Any(e => (string?)e.Attribute(X + "Name") == "MyNetDesktopButton"),
            "The MyNet launcher must move out of the main title bar.");
        Require(!File.ReadAllText(Path.Combine(sourceDirectory, "MainWindow.xaml.cs"))
            .Contains("OpenMyNetDesktopClick", StringComparison.Ordinal), "The retired title-bar handler must be removed.");
        Require((string?)entry.Attribute("Click") == "OpenDesktopClick", "MyNet must retain its panel click handler.");
        Require(File.ReadAllText(Path.Combine(sourceDirectory, "Controls", "AtlasMyNetView.xaml.cs"))
            .Contains("MyNetDesktopHost.OpenDesktop(new SettingsService());", StringComparison.Ordinal),
            "MyNet must use the installed desktop route.");
        var authoredHeader = entry.Ancestors(P + "Border").First();
        Require(authoredHeader.Descendants(P + "TextBlock").Any(e => (string?)e.Attribute("Text") == "MYNET"),
            "Open MyNet must belong to the MYNET side-panel header.");
        foreach (var (label, handler) in new[] { ("Add", "AddAppClick"), ("Import", "ImportAppsClick"), ("Export", "ExportAppsClick"), ("Refresh", "RefreshClick") })
            Require(authoredHeader.Descendants(P + "Button").Any(e => (string?)e.Attribute("Content") == label && (string?)e.Attribute("Click") == handler),
                $"The existing {label} action must keep its handler.");
        Directory.CreateDirectory(outputDirectory);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var appSource = XDocument.Load(Path.Combine(sourceDirectory, "App.xaml")).Root!;
        var resources = new XElement(P + "ResourceDictionary");
        CopyNamespaces(appSource, resources);
        resources.Add(appSource.Element(P + "Application.Resources")!.Elements().Select(e => new XElement(e)));
        Qualify(resources);
        resources.Descendants().Attributes("Click").Remove();
        app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString());

        var receipts = new List<object>();
        foreach (var width in new[] { 250d, 360d, 600d })
        {
            var markup = new XElement(authoredHeader);
            markup.DescendantsAndSelf().Attributes("Click").Remove();
            var wrapper = new XElement(P + "Grid");
            CopyNamespaces(source.Root!, wrapper);
            wrapper.Add(new XElement(P + "Grid.Resources", source.Root!.Element(P + "UserControl.Resources")!.Elements().Select(e => new XElement(e))));
            wrapper.Add(markup); Qualify(wrapper);
            var surface = (Grid)XamlReader.Parse(wrapper.ToString());
            surface.Width = width;
            surface.Background = (Brush)app.FindResource("AppBackgroundBrush");
            TextElement.SetFontFamily(surface, (FontFamily)app.FindResource("UiFontFamily"));
            TextElement.SetForeground(surface, (Brush)app.FindResource("ForegroundBrush"));
            surface.Measure(new Size(width, double.PositiveInfinity));
            surface.Height = Math.Ceiling(surface.DesiredSize.Height);
            surface.Measure(new Size(width, surface.Height)); surface.Arrange(new Rect(0, 0, width, surface.Height));
            surface.UpdateLayout(); surface.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); surface.UpdateLayout();
            var button = (Button)surface.FindName("MyNetDesktopButton");
            var header = (Border)surface.Children[0];
            var title = Descendants<TextBlock>(header).Single(t => t.Text == "MYNET");
            var actions = Descendants<Button>(header).Where(b => b != button).ToArray();
            Rect Bounds(FrameworkElement element) => element.TransformToAncestor(surface).TransformBounds(new Rect(element.RenderSize));
            var rect = Bounds(button); var headerBounds = Bounds(header); var titleBounds = Bounds(title);
            var label = $"{width}px MyNet side panel";
            // Detached render surfaces have no HWND, so authored visibility and
            // actual layout participation establish the visible-entry contract.
            Require(button.Visibility == Visibility.Visible && button.IsEnabled && button.ActualWidth >= 26,
                $"{label}: MyNet must be visible and usable ({button.Visibility}, {button.ActualWidth}).");
            Require(AutomationProperties.GetName(button) == "Open MyNet", $"{label}: MyNet must have an accessible name.");
            Require(headerBounds.Contains(rect) && rect.Top >= header.Padding.Top - .1 && rect.Bottom <= headerBounds.Bottom - header.Padding.Bottom + .1,
                $"{label}: MyNet exceeds the header.");
            Require(Math.Abs(rect.Right - (headerBounds.Right - header.Padding.Right)) <= .1,
                $"{label}: MyNet must align with the right edge of the header content.");
            Require(titleBounds.Right <= rect.Left && headerBounds.Contains(titleBounds), $"{label}: MyNet overlaps the heading.");
            var text = Descendants<TextBlock>(button).Single(t => t.Text == "Open MyNet");
            Require(rect.Contains(Bounds(text)), $"{label}: MyNet label is clipped.");
            foreach (var action in actions)
            {
                var bounds = Bounds(action);
                Require(headerBounds.Contains(bounds) && bounds.Right <= rect.Left && bounds.Top >= titleBounds.Bottom,
                    $"{label}: {action.Content} must remain below the heading and left of Open MyNet.");
                foreach (var other in actions.Where(other => other != action))
                {
                    var overlap = Rect.Intersect(bounds, Bounds(other));
                    Require(overlap.IsEmpty || overlap.Width < .1 || overlap.Height < .1,
                        $"{label}: {action.Content} overlaps {other.Content}.");
                }
            }
            receipts.Add(new { Width = width, MyNet = rect.ToString(), Header = headerBounds.ToString(), Heading = titleBounds.ToString(), Actions = actions.Select(a => new { Label = a.Content, Bounds = Bounds(a).ToString() }) });
            foreach (var scale in new[] { 1d, 1.5d, 2d })
            {
                var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(surface.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                bitmap.Render(surface);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(outputDirectory, $"mynet-panel-header-{width}-{scale * 100}.png"));
                encoder.Save(stream);
            }
        }
        File.WriteAllText(Path.Combine(outputDirectory, "mynet-shell-entry.json"), JsonSerializer.Serialize(new { Checks = checks, Cases = receipts }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"MYNET_SHELL_ENTRY_OK|checks={checks}|cases={receipts.Count}|actual-source-panel-header|isolated-render-only");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void CopyNamespaces(XElement source, XElement target)
    {
        foreach (var attribute in source.Attributes().Where(a => a.IsNamespaceDeclaration)) target.Add(new XAttribute(attribute));
    }

    private static void Qualify(XElement root)
    {
        foreach (var attribute in root.DescendantsAndSelf().Attributes().Where(a => a.IsNamespaceDeclaration))
            if (attribute.Value.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal) && !attribute.Value.Contains(";assembly=", StringComparison.Ordinal)) attribute.Value += ";assembly=Hypershell";
        foreach (var element in root.DescendantsAndSelf())
            if (element.Name.NamespaceName.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal) && !element.Name.NamespaceName.Contains(";assembly=", StringComparison.Ordinal))
                element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=Hypershell");
    }
}
