using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hypershell.ViewModels;

namespace Hypershell;

/// <summary>Interactive staging shows page regions and empty object slots rather than sample content.</summary>
public sealed class HyperlibTemplateStageView : ContentControl
{
    private HyperlibWizardTemplateViewModel? _model;
    public HyperlibTemplateStageView()
    {
        DataContextChanged += (_, _) => Connect();
        Loaded += (_, _) => Connect();
        Unloaded += (_, _) => { if (_model is not null) _model.PropertyChanged -= Changed; _model = null; };
    }
    private void Connect()
    {
        if (_model is not null) _model.PropertyChanged -= Changed;
        _model = DataContext as HyperlibWizardTemplateViewModel;
        if (_model is not null) _model.PropertyChanged += Changed;
        Draw();
    }
    private void Changed(object? sender, PropertyChangedEventArgs args) => Draw();
    private void Draw()
    {
        if (_model is not { } model) { Content = null; return; }
        var background = (SolidColorBrush)model.PreviewBackground;
        var light = background.Color.R * .299 + background.Color.G * .587 + background.Color.B * .114 > 165;
        var ink = new SolidColorBrush(light ? Color.FromRgb(31, 45, 56) : Color.FromRgb(232, 241, 239));
        var accent = model.PreviewAccent;
        var showNavigation = model.IncludeNavigation && model.NavigationPlacement != "none";
        var navigationRegion = model.NavigationPlacement == "top" ? "header" : model.NavigationPlacement == "bottom" ? "footer" : "sidebar";
        var canvas = new StackPanel();
        var shell = new Border { Background = background, BorderBrush = accent, BorderThickness = new(1), CornerRadius = new(7), Padding = new(16), Child = canvas };
        Border Slot(string label, string objectName, string region, double height = 40)
        {
            var names = model.RepeatingObjects.Where(item => item.Region == region).Select(item => item.SelectedObject.Title).ToArray();
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = accent, Margin = new(0, 0, 0, 5) });
            if (objectName.Length > 0) content.Children.Add(new TextBlock { Text = objectName, Foreground = ink, FontSize = 11, TextWrapping = TextWrapping.Wrap });
            if (showNavigation && navigationRegion == region)
                content.Children.Add(new TextBlock { Text = "NAVIGATION · " + (model.SelectedNavigation.Id is null ? "Work · future Pages" : model.SelectedNavigation.Title),
                    Foreground = ink, FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
            foreach (var name in names) content.Children.Add(new TextBlock { Text = "+ " + name + " · empty slot", Foreground = ink, FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0) });
            return new Border { BorderBrush = accent, BorderThickness = new(1), CornerRadius = new(4), Padding = new(10), MinHeight = height, Child = content, Margin = new(0, 0, 0, 10) };
        }
        if (model.HeaderPlacement != "none" || model.RepeatingObjects.Any(item => item.Region == "header") || showNavigation && navigationRegion == "header")
            canvas.Children.Add(Slot("HEADER", model.HeaderPlacement == "none" ? "" : model.SelectedHeader.Title, "header"));
        Border Navigation() => Slot("SIDEBAR", "", "sidebar");
        var center = new Grid();
        var side = showNavigation && model.NavigationPlacement is "left" or "right";
        center.ColumnDefinitions.Add(new() { Width = new(side && model.NavigationPlacement == "left" ? 1 : 3, GridUnitType.Star) });
        if (side) center.ColumnDefinitions.Add(new() { Width = new(model.NavigationPlacement == "left" ? 3 : 1, GridUnitType.Star) });
        var main = Slot("MAIN CONTENT · " + model.ContentAlignment.ToUpperInvariant() + " · " + model.ContentWidth.ToUpperInvariant(),
            model.SelectedBody.Id is null ? "Empty content area\nAdd objects and write content in Phase 3" : model.SelectedBody.Title + " · empty content slot", "main", 112);
        main.HorizontalAlignment = model.ContentWidth == "full" ? HorizontalAlignment.Stretch : model.ContentAlignment switch { "center" => HorizontalAlignment.Center, "right" => HorizontalAlignment.Right, _ => HorizontalAlignment.Left };
        main.MinWidth = 140; main.MaxWidth = model.ContentWidth == "comfortable" ? 620 : double.PositiveInfinity;
        if (model.ContentWidth != "full") main.Width = double.NaN;
        Grid.SetColumn(main, side && model.NavigationPlacement == "left" ? 1 : 0); center.Children.Add(main);
        if (side)
        {
            var navigation = Navigation(); navigation.Margin = model.NavigationPlacement == "left" ? new(0, 0, 10, 10) : new(10, 0, 0, 10);
            Grid.SetColumn(navigation, model.NavigationPlacement == "left" ? 0 : 1); center.Children.Add(navigation);
        }
        else if (model.RepeatingObjects.Any(item => item.Region == "sidebar"))
        {
            center.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var sidebar = Slot("SIDEBAR", "Repeating elements", "sidebar"); Grid.SetColumn(sidebar, 1); sidebar.Margin = new(10, 0, 0, 10); center.Children.Add(sidebar);
        }
        canvas.Children.Add(center);
        if (model.FooterPlacement != "none" || model.RepeatingObjects.Any(item => item.Region == "footer") || showNavigation && navigationRegion == "footer")
            canvas.Children.Add(Slot("FOOTER", model.FooterPlacement == "none" ? "" : model.SelectedFooter.Title, "footer"));
        Content = shell;
    }
}
