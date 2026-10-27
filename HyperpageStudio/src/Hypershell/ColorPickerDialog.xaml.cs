using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Hypershell;

public partial class ColorPickerDialog : Window
{
    private static readonly string[] Palette =
    [
        "#0D1117", "#161B22", "#21262D", "#30363D", "#6E7681", "#8B949E", "#C9D1D9", "#F0F6FC",
        "#3B2015", "#5A2F1F", "#8A451F", "#D96B20", "#FF9F43", "#F2CC60", "#E3B341", "#7A5C16",
        "#4C1D1D", "#7A263A", "#B13A4A", "#FF7B72", "#D05B9C", "#A371F7", "#6E40C9", "#3C1E70",
        "#0F2942", "#1F4E79", "#1F6FEB", "#58A6FF", "#39C5CF", "#56D4DD", "#1B6B70", "#163A46",
        "#16351F", "#245C2D", "#238636", "#3FB950", "#7EE787", "#A8D26D", "#6E7F2A", "#39451D"
    ];

    private bool _isUpdating;

    public ColorPickerDialog(string purpose, string initialColor)
    {
        InitializeComponent();
        PickerTitleText.Text = $"{purpose.ToUpperInvariant()} // COLOR SELECTOR";
        PaletteItemsControl.ItemsSource = Palette;
        SetColor(ParseColor(initialColor) ?? Color.FromRgb(40, 49, 61));
    }

    public string SelectedColor { get; private set; } = "#28313D";

    private void PaletteColorClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: string value }
            && ParseColor(value) is { } color)
        {
            SetColor(color);
        }
    }

    private void RgbSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> eventArgs)
    {
        if (!_isUpdating && RedSlider is not null && GreenSlider is not null && BlueSlider is not null)
        {
            RefreshColor();
        }
    }

    private void SetColor(Color color)
    {
        _isUpdating = true;
        RedSlider.Value = color.R;
        GreenSlider.Value = color.G;
        BlueSlider.Value = color.B;
        _isUpdating = false;
        RefreshColor();
    }

    private void RefreshColor()
    {
        var color = Color.FromRgb(
            (byte)Math.Round(RedSlider.Value),
            (byte)Math.Round(GreenSlider.Value),
            (byte)Math.Round(BlueSlider.Value));
        SelectedColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        SelectedColorPreview.Background = new SolidColorBrush(color);
        SelectedColorText.Text = SelectedColor;
        SelectedColorText.Foreground = new SolidColorBrush(ContrastingColor(color));
        RedValueText.Text = color.R.ToString();
        GreenValueText.Text = color.G.ToString();
        BlueValueText.Text = color.B.ToString();
    }

    private void UseColorClick(object sender, RoutedEventArgs eventArgs)
    {
        DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs eventArgs)
    {
        DialogResult = false;
    }

    private void TitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private static Color? ParseColor(string value)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(value)!;
        }
        catch
        {
            return null;
        }
    }

    private static Color ContrastingColor(Color color)
    {
        var luminance = ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255d;
        return luminance > 0.58
            ? Color.FromRgb(13, 17, 23)
            : Color.FromRgb(246, 248, 250);
    }
}
