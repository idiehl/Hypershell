using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Hypershell.Controls;

/// <summary>The shared mint shell and spiral trails with a red, green, and blue cube.</summary>
public sealed class AiShellGlyph : FrameworkElement
{
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(AiShellGlyph),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0xA3, 0xC7, 0xBD)),
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    // Matches Assets/Brand/hypershell-spiral-shell.svg. The standalone vector
    // rendering tool compares the actual control with that SVG at every icon size.
    private static readonly GlyphLayer[] Layers =
    [
        new(Shape("M 17 5.8 C 21.7 7.2 23.7 12.8 20.8 17.1 C 17.9 21.4 10.9 22.9 2.7 20.3 C 12.2 22.5 19.2 18.4 20.5 13.8 C 21.7 9.8 19.8 6.9 17 5.8 Z"), .68),
        new(Shape("M 7 18.2 C 2.3 16.8 .3 11.2 3.2 6.9 C 6.1 2.6 13.1 1.1 21.3 3.7 C 11.8 1.5 4.8 5.6 3.5 10.2 C 2.3 14.2 4.2 17.1 7 18.2 Z"), .68),
        new(Shape("M 12 3.8 C 7.4 3.8 4.8 7.1 4.8 11.9 C 4.8 16.7 7.5 20.2 12 20.2 C 16.5 20.2 19.2 16.7 19.2 11.9 C 19.2 7.1 16.6 3.8 12 3.8 Z"), .12),
        new(Shape("M 12 3.8 C 7.4 3.8 4.8 7.1 4.8 11.9 C 4.8 16.7 7.5 20.2 12 20.2 C 16.5 20.2 19.2 16.7 19.2 11.9 C 19.2 7.1 16.6 3.8 12 3.8 Z"), 1, 1.3),
        new(Shape("M 8.5 4.7 L 9.95 8.35 M 15.5 4.7 L 14.05 8.35 M 19.2 11.9 L 16.1 11.9 M 15.5 19.3 L 14.05 15.45 M 8.5 19.3 L 9.95 15.45 M 4.8 11.9 L 7.9 11.9"), .68, .95),
        new(Shape("M 7.9 11.9 L 9.95 8.35 L 14.05 8.35 L 11.6 11.9 Z"), 1, Color: Color.FromRgb(0xEF, 0x5D, 0x66)),
        new(Shape("M 7.9 11.9 L 11.6 11.9 L 14.05 15.45 L 9.95 15.45 Z"), 1, Color: Color.FromRgb(0x55, 0xC9, 0x8A)),
        new(Shape("M 11.6 11.9 L 14.05 8.35 L 16.1 11.9 L 14.05 15.45 Z"), 1, Color: Color.FromRgb(0x4B, 0x8E, 0xF1)),
        new(Shape("M 7.9 11.9 L 9.95 8.35 L 14.05 8.35 L 16.1 11.9 L 14.05 15.45 L 9.95 15.45 Z"), .92, .95),
        new(Shape("M 7.9 11.9 L 11.6 11.9 L 14.05 8.35 M 11.6 11.9 L 14.05 15.45"), .92, .9)
    ];

    // Retained for XAML/API compatibility. Brand colors are fixed and do not
    // inherit control tints; only the original layer opacities affect the shell.
    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        Math.Min(24, availableSize.Width), Math.Min(24, availableSize.Height));

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var scale = Math.Min(RenderSize.Width, RenderSize.Height) / 24;
        if (scale <= 0)
        {
            return;
        }

        drawingContext.PushTransform(new MatrixTransform(scale, 0, 0, scale,
            (RenderSize.Width - 24 * scale) / 2, (RenderSize.Height - 24 * scale) / 2));
        foreach (var layer in Layers)
        {
            var brush = new SolidColorBrush(layer.Color ?? Color.FromRgb(0xA3, 0xC7, 0xBD)) { Opacity = layer.Opacity };
            drawingContext.DrawGeometry(
                layer.StrokeWidth == 0 ? brush : null,
                layer.StrokeWidth > 0 ? Stroke(brush, layer.StrokeWidth) : null,
                layer.Geometry);
        }
        drawingContext.Pop();
    }

    private static Pen Stroke(Brush brush, double width) => new(brush, width)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
        LineJoin = PenLineJoin.Round
    };

    private static Geometry Shape(string path)
    {
        var geometry = Geometry.Parse(path);
        geometry.Freeze();
        return geometry;
    }

    private sealed record GlyphLayer(Geometry Geometry, double Opacity, double StrokeWidth = 0, Color? Color = null);
}
