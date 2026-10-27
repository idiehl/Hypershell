using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Hypershell.Extensibility;

namespace Hypershell.Controls;

/// <summary>Small, theme-aware symbols for workspace navigation.</summary>
public sealed class WorkspaceSideToolGlyph : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(WorkspaceSideToolKind), typeof(WorkspaceSideToolGlyph),
        new FrameworkPropertyMetadata(default(WorkspaceSideToolKind), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(WorkspaceSideToolGlyph), new FrameworkPropertyMetadata(Brushes.White,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    // A project folder containing a small board, distinct from the plain file browser.
    private static readonly Geometry Project = Shape(
        "M 3 6.5 L 3 5 Q 3 3.5 4.5 3.5 L 9 3.5 L 11 6 L 19.5 6 Q 21 6 21 7.5 "
        + "L 21 19 Q 21 20.5 19.5 20.5 L 4.5 20.5 Q 3 20.5 3 19 Z "
        + "M 7 10.5 L 10 10.5 L 10 16.5 L 7 16.5 Z M 14 10.5 L 17 10.5 M 14 13.5 L 17 13.5 M 14 16.5 L 17 16.5");
    private static readonly Geometry RadioTower = Shape(
        "M 7.5 21 L 12 8 L 16.5 21 M 9.3 16 L 14.7 16 M 8.4 19 L 15.6 19 M 6 21 L 18 21 "
        + "M 8.1 11.1 C 5.9 9 5.9 5.8 8.1 3.8 M 15.9 3.8 C 18.1 5.8 18.1 9 15.9 11.1 "
        + "M 4.7 14 C 0.9 10.3 0.9 4.7 4.7 1.3 M 19.3 1.3 C 23.1 4.7 23.1 10.3 19.3 14");
    private static readonly Geometry Eye = Shape(
        "M 1.8 12 C 4.5 7.4 7.8 5.3 12 5.3 C 16.2 5.3 19.5 7.4 22.2 12 "
        + "C 19.5 16.6 16.2 18.7 12 18.7 C 7.8 18.7 4.5 16.6 1.8 12 Z");

    public WorkspaceSideToolKind Kind
    {
        get => (WorkspaceSideToolKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        Math.Min(24, availableSize.Width), Math.Min(24, availableSize.Height));

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var geometry = Kind switch
        {
            WorkspaceSideToolKind.ProjectWorkItems => Project,
            WorkspaceSideToolKind.AtlasMyNet => RadioTower,
            WorkspaceSideToolKind.NetworkObservatory => Eye,
            _ => null
        };
        var scale = Math.Min(RenderSize.Width, RenderSize.Height) / 24;
        if (geometry is null || scale <= 0 || Foreground is null) return;
        context.PushTransform(new MatrixTransform(scale, 0, 0, scale,
            (RenderSize.Width - 24 * scale) / 2, (RenderSize.Height - 24 * scale) / 2));
        var pen = new Pen(Foreground, 1.4)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        context.DrawGeometry(null, pen, geometry);
        if (Kind == WorkspaceSideToolKind.AtlasMyNet)
            context.DrawEllipse(Foreground, null, new Point(12, 7.4), 1.6, 1.6);
        if (Kind == WorkspaceSideToolKind.NetworkObservatory)
        {
            context.DrawEllipse(null, pen, new Point(12, 12), 3.3, 3.3);
            context.DrawEllipse(Foreground, null, new Point(12, 12), 1.1, 1.1);
        }
        context.Pop();
    }

    private static Geometry Shape(string path)
    {
        var geometry = Geometry.Parse(path);
        geometry.Freeze();
        return geometry;
    }
}
