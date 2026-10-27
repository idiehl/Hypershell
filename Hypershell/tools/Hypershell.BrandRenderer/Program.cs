using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Hypershell.Controls;

internal static class Program
{
    private static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    [STAThread]
    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Brand rendering failed: {error.Message}");
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: Hypershell.BrandRenderer <repository-root> <output-directory>");
            return 2;
        }

        var root = Path.GetFullPath(args[0]);
        var output = Path.GetFullPath(args[1]);
        var brand = Path.Combine(root, "src", "Hypershell", "Assets", "Brand");
        var source = Path.Combine(brand, "hypershell-spiral-shell.svg");
        var approved = Path.Combine(root, "doc", "design", "ai-shell-cube", "revision-02");
        var svg = XDocument.Load(source).Root ?? throw new InvalidDataException("SVG is empty.");
        if ((string?)svg.Attribute("viewBox") != "0 0 24 24"
            || svg.Elements().Count(element => element.Name.LocalName == "path") != 10)
            throw new InvalidDataException("Expected the approved 24-unit, ten-layer vector.");
        var active = (Color)ColorConverter.ConvertFromString("#A3C7BD");
        var idle = (Color)ColorConverter.ConvertFromString("#A5B7BE");
        AssertBrandPalette(svg);
        var historicalSvg = Path.Combine(approved, "ai-shell-cube-spiral.svg");
        if (File.Exists(historicalSvg))
            AssertHistoricalGeometry(svg, XDocument.Load(historicalSvg).Root
                ?? throw new InvalidDataException("Historical SVG is empty."));
        Directory.CreateDirectory(output);

        var natural = new AiShellGlyph();
        natural.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        if (natural.DesiredSize != new Size(24, 24))
            throw new InvalidDataException("The shared glyph's natural size changed.");

        foreach (var size in IconSizes.Concat([192, 512]))
        {
            var native = RenderNative(size, active);
            AssertSamePixels(native, RenderSvg(svg, size, active), $"native/SVG {size}px active");
            var idleNative = RenderNative(size, idle);
            AssertSamePixels(idleNative, RenderSvg(svg, size, idle), $"native/SVG {size}px idle");
            AssertSamePixels(native, idleNative, $"fixed brand palette {size}px active/idle");
            var path = Path.Combine(output, $"hypershell-{size}.png");
            SavePng(native, path);
            AssertTransparentCorners(native, size);
            if (size == 512)
            {
                AssertFacePixels(native);
                File.Copy(path, Path.Combine(brand, "hypershell-spiral-shell.png"), overwrite: true);
            }
        }

        var icon = Path.Combine(output, "hypershell.ico");
        WriteIcon(output, icon);
        VerifyIcon(output, icon);
        Console.WriteLine("BRAND_VECTOR_MATCH_OK sizes=16,20,24,32,40,48,64,128,192,256,512 colors=active,idle");
        Console.WriteLine(File.Exists(historicalSvg)
            ? "BRAND_HISTORICAL_GEOMETRY_OK paths=10 nonface_opacity=preserved"
            : "BRAND_HISTORICAL_GEOMETRY_SKIPPED reason=historical_svg_unavailable");
        Console.WriteLine("BRAND_PALETTE_OK shell=#A3C7BD faces=#EF5D66,#55C98A,#4B8EF1 inherited_tint=ignored");
        Console.WriteLine("BRAND_ICON_FRAMES_OK sizes=16,20,24,32,40,48,64,128,256 transparent=true");
        Console.WriteLine($"SVG_SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)))}");
        Console.WriteLine($"ICON_SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(icon)))}");
        Console.WriteLine($"Output: {output}");
        return 0;
    }

    private static RenderTargetBitmap RenderNative(int size, Color color)
    {
        var glyph = new AiShellGlyph { Width = size, Height = size, Foreground = new SolidColorBrush(color) };
        glyph.Measure(new Size(size, size));
        glyph.Arrange(new Rect(0, 0, size, size));
        glyph.UpdateLayout();
        return Render(glyph, size);
    }

    private static RenderTargetBitmap RenderSvg(XElement svg, int size, Color color)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.PushTransform(new ScaleTransform(size / 24d, size / 24d));
            foreach (var path in svg.Elements().Where(element => element.Name.LocalName == "path"))
            {
                var geometry = Geometry.Parse((string?)path.Attribute("d") ?? throw new InvalidDataException("Missing SVG path."));
                var fill = SvgBrush(path, "fill", color);
                var strokeBrush = SvgBrush(path, "stroke", color);
                Pen? stroke = strokeBrush is not null
                    ? new Pen(strokeBrush, Number(path, "stroke-width", 1))
                    {
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round,
                        LineJoin = PenLineJoin.Round
                    } : null;
                drawing.DrawGeometry(fill, stroke, geometry);
            }
            drawing.Pop();
        }
        return Render(visual, size);
    }

    private static Brush? SvgBrush(XElement path, string attribute, Color inheritedColor)
    {
        var value = (string?)path.Attribute(attribute);
        if (string.IsNullOrEmpty(value) || value == "none") return null;
        var color = value == "currentColor" ? inheritedColor : (Color)ColorConverter.ConvertFromString(value);
        return new SolidColorBrush(color) { Opacity = Number(path, $"{attribute}-opacity", 1) };
    }

    private static void AssertBrandPalette(XElement svg)
    {
        var paths = svg.Elements().Where(element => element.Name.LocalName == "path").ToArray();
        string[] faces = ["#EF5D66", "#55C98A", "#4B8EF1"];
        for (var index = 0; index < paths.Length; index++)
        {
            var path = paths[index];
            if (index is >= 5 and <= 7)
            {
                if ((string?)path.Attribute("fill") != faces[index - 5] || Number(path, "fill-opacity", 1) != 1)
                    throw new InvalidDataException($"Cube face {index - 5} must use its opaque RGB brand color.");
            }
            else
            {
                foreach (var attribute in new[] { "fill", "stroke" })
                {
                    var value = (string?)path.Attribute(attribute);
                    if (value is not null and not "none" and not "#A3C7BD")
                        throw new InvalidDataException($"Non-face {attribute} must be fixed mint.");
                }
            }
        }
    }

    private static void AssertHistoricalGeometry(XElement current, XElement historical)
    {
        var currentPaths = current.Elements().Where(element => element.Name.LocalName == "path").ToArray();
        var historicalPaths = historical.Elements().Where(element => element.Name.LocalName == "path").ToArray();
        if (currentPaths.Length != historicalPaths.Length)
            throw new InvalidDataException("Historical layer count changed.");
        foreach (var attribute in new[] { "viewBox", "stroke-linecap", "stroke-linejoin" })
            if ((string?)current.Attribute(attribute) != (string?)historical.Attribute(attribute))
                throw new InvalidDataException($"Historical {attribute} changed.");
        for (var index = 0; index < currentPaths.Length; index++)
        {
            foreach (var attribute in new[] { "d", "stroke-width" })
                if ((string?)currentPaths[index].Attribute(attribute) != (string?)historicalPaths[index].Attribute(attribute))
                    throw new InvalidDataException($"Historical layer {index} {attribute} changed.");
            if (index is >= 5 and <= 7) continue;
            foreach (var attribute in new[] { "fill-opacity", "stroke-opacity" })
                if (Number(currentPaths[index], attribute, 1) != Number(historicalPaths[index], attribute, 1))
                    throw new InvalidDataException($"Historical layer {index} {attribute} changed.");
        }
    }

    private static void AssertFacePixels(BitmapSource bitmap)
    {
        var bytes = Pixels(bitmap);
        (double X, double Y, string Color)[] samples = [(11, 10, "#EF5D66"), (11, 14, "#55C98A"), (14.2, 11.9, "#4B8EF1")];
        foreach (var sample in samples)
        {
            var expected = (Color)ColorConverter.ConvertFromString(sample.Color);
            var index = ((int)(sample.Y * bitmap.PixelHeight / 24) * bitmap.PixelWidth
                + (int)(sample.X * bitmap.PixelWidth / 24)) * 4;
            if (bytes[index] != expected.B || bytes[index + 1] != expected.G
                || bytes[index + 2] != expected.R || bytes[index + 3] != 255)
                throw new InvalidDataException($"Rendered cube face is not opaque {sample.Color}.");
        }
    }

    private static double Number(XElement element, string name, double fallback) =>
        double.TryParse((string?)element.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value : fallback;

    private static RenderTargetBitmap Render(Visual visual, int size)
    {
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static byte[] Pixels(BitmapSource bitmap)
    {
        var normalized = new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0);
        var bytes = new byte[normalized.PixelWidth * normalized.PixelHeight * 4];
        normalized.CopyPixels(bytes, normalized.PixelWidth * 4, 0);
        return bytes;
    }

    private static void AssertSamePixels(BitmapSource first, BitmapSource second, string description)
    {
        if (first.PixelWidth != second.PixelWidth || first.PixelHeight != second.PixelHeight
            || !Pixels(first).AsSpan().SequenceEqual(Pixels(second)))
            throw new InvalidDataException($"Pixel mismatch: {description}.");
    }

    private static void AssertTransparentCorners(BitmapSource bitmap, int size)
    {
        var bytes = Pixels(bitmap);
        if (new[] { 0, size - 1, size * (size - 1), size * size - 1 }.Any(index => bytes[index * 4 + 3] != 0))
            throw new InvalidDataException($"A {size}px icon corner is not transparent.");
    }

    private static void SavePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static BitmapSource LoadPng(string path)
    {
        using var stream = File.OpenRead(path);
        return new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
    }

    private static void WriteIcon(string directory, string path)
    {
        var frames = IconSizes.Select(size => File.ReadAllBytes(Path.Combine(directory, $"hypershell-{size}.png"))).ToArray();
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Length);
        var offset = 6 + 16 * frames.Length;
        for (var index = 0; index < frames.Length; index++)
        {
            writer.Write((byte)(IconSizes[index] == 256 ? 0 : IconSizes[index]));
            writer.Write((byte)(IconSizes[index] == 256 ? 0 : IconSizes[index]));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)frames[index].Length);
            writer.Write((uint)offset);
            offset += frames[index].Length;
        }
        foreach (var frame in frames) writer.Write(frame);
    }

    private static void VerifyIcon(string directory, string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1 || reader.ReadUInt16() != IconSizes.Length)
            throw new InvalidDataException("Invalid icon header.");
        foreach (var size in IconSizes)
        {
            var dimension = size == 256 ? 0 : size;
            if (reader.ReadByte() != dimension || reader.ReadByte() != dimension || reader.ReadByte() != 0
                || reader.ReadByte() != 0 || reader.ReadUInt16() != 1 || reader.ReadUInt16() != 32)
                throw new InvalidDataException($"Invalid {size}px icon directory entry.");
            var length = reader.ReadUInt32();
            var offset = reader.ReadUInt32();
            var resume = reader.BaseStream.Position;
            reader.BaseStream.Position = offset;
            var frame = reader.ReadBytes(checked((int)length));
            if (!frame.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(directory, $"hypershell-{size}.png"))))
                throw new InvalidDataException($"Packaged {size}px PNG changed.");
            reader.BaseStream.Position = resume;
        }
    }
}
