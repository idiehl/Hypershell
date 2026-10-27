using Hypershell.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Hypershell;

public sealed class StudioFileHelpWindow : Window
{
    public StudioFileHelpWindow(StudioProjectFileHelp help)
    {
        Title = "About " + help.Title;
        Width = 620; Height = 590; MinWidth = 420; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppBackgroundBrush");
        SetResourceReference(ForegroundProperty, "ForegroundBrush");
        var root = new DockPanel { Margin = new Thickness(26) };
        var close = new Button { Content = "Close", MinWidth = 86, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0), IsCancel = true };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Bottom); root.Children.Add(close);
        var body = new StackPanel();
        body.Children.Add(Text(help.Title, 23, FontWeights.SemiBold));
        var path = Text(help.RelativePath, 12, FontWeights.Normal); path.Opacity = .7; path.Margin = new Thickness(0, 8, 0, 5); body.Children.Add(path);
        var ownership = Text(help.Ownership, 12, FontWeights.Medium); ownership.Opacity = .8; body.Children.Add(ownership);
        AddSection("Purpose", help.Purpose);
        AddSection("How to edit it", help.EditGuidance);
        AddSection("Where to begin", help.GettingStarted);
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        void AddSection(string title, string text)
        {
            var heading = Text(title, 15, FontWeights.SemiBold); heading.Margin = new Thickness(0, 24, 0, 7); body.Children.Add(heading);
            body.Children.Add(Text(text, 14, FontWeights.Normal));
        }
    }

    private static TextBlock Text(string value, double size, FontWeight weight) => new()
    {
        Text = value, FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap,
        LineHeight = size * 1.5
    };
}
