using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Hypershell.Models;

namespace Hypershell.Services;

public sealed record HyperlibBackgroundDesign(string Id, string Title, string Description, string DefaultColor, string ThumbnailFile);

/// <summary>Portable, deterministic web surfaces. The native cards and Page renderer share these exact designs.</summary>
public static class HyperlibBackgroundDesigns
{
    public static IReadOnlyList<HyperlibBackgroundDesign> All { get; } = Array.AsReadOnly(new[]
    {
        Design("atlas-night", "Atlas night", "The original Atlas Universalis deep-blue sky, with its twelve scattered stars.", "#060e1f"),
        Design("atlas-sparse", "Quiet stars", "A spacious midnight sky with a few small lights and a soft horizon.", "#091528"),
        Design("atlas-dense", "Far constellation", "A finer, denser field of stars over a cool, even night sky.", "#071425"),
        Design("atlas-nebula", "Nebula dusk", "Gentle blue and violet atmospheric light behind an irregular star field.", "#111528"),
        Design("paper-grain", "Cotton paper", "Warm, lightly flecked paper for reading and editorial layouts.", "#f5f1e8"),
        Design("linen-woven", "Linen weave", "A quiet woven surface with fine crossing threads and a soft mineral tone.", "#e8ece9"),
        Design("blueprint-grid", "Drafting grid", "A restrained technical grid with fine subdivisions and a cool blue-green ground.", "#102c37"),
        Design("contour-lines", "Contour study", "Flowing topographic lines with generous open space and a warm stone ground.", "#e8e3dc"),
        Design("aurora-mist", "Aurora mist", "Soft teal and lavender light drifting across a dark, uncluttered surface.", "#111f2a"),
        Design("quiet-dunes", "Quiet dunes", "Broad, understated sand-colored curves for calm portfolios and stories.", "#e5d6bd")
    });

    public static HyperlibBackgroundDesign? Find(string? id) => All.FirstOrDefault(item => item.Id == id);

    private static HyperlibBackgroundDesign Design(string id, string title, string description, string color)
        => new(id, title, description, color, "Assets/HyperlibBackgrounds/" + id + ".png");

    /// <summary>Only validated colors, local media paths, and known geometry become inline CSS.</summary>
    public static string SurfaceCss(string baseColor, HyperlibPageBackgroundDefinition surface)
    {
        if (surface is null) throw new InvalidDataException("The Page background settings are missing.");
        if (surface.ImagePath is null) throw new InvalidDataException("The background image path is missing.");
        if (!Color(baseColor)) throw new InvalidDataException("Choose a six-digit hexadecimal background color.");
        if (surface.DesignId is not ("solid" or "image") && Find(surface.DesignId) is null)
            throw new InvalidDataException("Choose a background from this project's supported designs.");
        if (surface.ImageFit is not ("cover" or "contain" or "tile")) throw new InvalidDataException("Choose Cover, Contain, or Tile for the background image.");
        if (!Color(surface.BorderColor)) throw new InvalidDataException("Choose a six-digit hexadecimal Page border color.");
        if (!double.IsFinite(surface.BorderWidth) || surface.BorderWidth is < 0 or > 20
            || !double.IsFinite(surface.BorderRadius) || surface.BorderRadius is < 0 or > 200)
            throw new InvalidDataException("Choose a Page border width from 0 to 20 and corner radius from 0 to 200.");
        if (surface.BorderStyle is not ("none" or "solid" or "dashed" or "dotted" or "double"))
            throw new InvalidDataException("Choose a supported Page border style.");
        if (!string.IsNullOrEmpty(surface.ImagePath) && !SafeImagePath(surface.ImagePath))
            throw new InvalidDataException("Import the background image into this project's Media folder before saving.");

        var paper = "var(--hl-paper," + baseColor + ")";
        var (image, size, repeat, position) = Layers(surface.DesignId, paper);
        if (surface.DesignId == "image" && surface.ImagePath.Length > 0)
        {
            image = "url(\"" + surface.ImagePath + "\")";
            size = surface.ImageFit == "tile" ? "auto" : surface.ImageFit;
            repeat = surface.ImageFit == "tile" ? "repeat" : "no-repeat";
            position = "center";
        }
        return "background-color:" + paper + ";background-image:" + image + ";background-size:" + size
            + ";background-repeat:" + repeat + ";background-position:" + position
            + ";background-attachment:scroll;background-origin:padding-box;background-clip:padding-box;background-blend-mode:normal;"
            + "border:" + Number(surface.BorderWidth) + "px " + surface.BorderStyle + " " + surface.BorderColor
            + ";border-radius:" + Number(surface.BorderRadius) + "px;box-sizing:border-box;";
    }

    public static string ThumbnailHtml(string id, int width = 480, int height = 270)
    {
        var design = Find(id) ?? throw new InvalidDataException("Unknown background design.");
        if (width is < 80 or > 2400 || height is < 60 or > 1800) throw new ArgumentOutOfRangeException(nameof(width));
        var css = SurfaceCss(design.DefaultColor, new() { DesignId = id });
        return "<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><style>*{box-sizing:border-box}html,body{margin:0;width:100%;height:100%}.surface{width:100%;height:100%;" + css + "}</style></head><body><div class='surface' data-background-design='" + WebUtility.HtmlEncode(id) + "'></div></body></html>";
    }

    private static bool Color(string? value) => value is not null && Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant);
    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static bool SafeImagePath(string path) => path.StartsWith("/media/", StringComparison.Ordinal)
        && path.Length <= 240 && Regex.IsMatch(path, "^/media/[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)*$", RegexOptions.CultureInvariant)
        && !path.Split('/').Any(part => part is "." or "..");

    private static (string Image, string Size, string Repeat, string Position) Layers(string id, string paper)
    {
        string Mix(string color, int percentage) => "color-mix(in srgb," + paper + " " + (100 - percentage) + "%," + color + ")";
        return id switch
        {
            "atlas-night" => (AtlasStars + ",radial-gradient(circle at 50% 44%," + Mix("#34559c", 24) + " 0%," + Mix("#1f2c74", 20) + " 38%," + Mix("#142347", 15) + " 66%," + paper + " 100%)", "auto", "no-repeat", "center"),
            "atlas-sparse" => (QuietStars + ",radial-gradient(ellipse at 62% 14%," + Mix("#536b9d", 17) + " 0%,transparent 70%)", "auto", "no-repeat", "center"),
            "atlas-dense" => (Svg(StarTile(720, 560, 96, 7741, .8)) + ",radial-gradient(ellipse at 48% 45%," + Mix("#324d87", 21) + " 0%,transparent 78%)", "720px 560px,auto", "repeat,no-repeat", "center,center"),
            "atlas-nebula" => (Svg(StarTile(880, 640, 49, 5171, .68)) + ",radial-gradient(ellipse at 18% 22%,rgba(68,92,164,.24),transparent 62%),radial-gradient(ellipse at 82% 68%,rgba(131,72,154,.18),transparent 66%),radial-gradient(ellipse at 62% 12%,rgba(84,128,171,.1),transparent 64%)", "880px 640px,auto,auto,auto", "repeat,no-repeat,no-repeat,no-repeat", "center,center,center,center"),
            "paper-grain" => (Svg(PaperGrain()) + ",linear-gradient(125deg,rgba(255,255,255,.32),rgba(107,80,42,.025))", "128px 128px,auto", "repeat,no-repeat", "left top,center"),
            "linen-woven" => (Svg(LinenWeave()) + ",linear-gradient(145deg,rgba(255,255,255,.18),rgba(40,62,55,.025))", "4px 4px,auto", "repeat,no-repeat", "left top,center"),
            "blueprint-grid" => ("linear-gradient(rgba(174,221,230,.12) 1px,transparent 1px),linear-gradient(90deg,rgba(174,221,230,.12) 1px,transparent 1px),linear-gradient(rgba(174,221,230,.035) 1px,transparent 1px),linear-gradient(90deg,rgba(174,221,230,.035) 1px,transparent 1px),radial-gradient(ellipse at 16% 12%,rgba(96,170,184,.12),transparent 70%)", "48px 48px,48px 48px,12px 12px,12px 12px,auto", "repeat,repeat,repeat,repeat,no-repeat", "left top,left top,left top,left top,center"),
            "contour-lines" => (Svg(Contours()), "cover", "no-repeat", "center"),
            "aurora-mist" => ("radial-gradient(ellipse at 12% 0%,rgba(64,157,148,.3),transparent 56%),radial-gradient(ellipse at 84% 38%,rgba(132,112,178,.2),transparent 60%),radial-gradient(ellipse at 42% 100%,rgba(77,118,147,.18),transparent 60%)", "auto", "no-repeat", "center"),
            "quiet-dunes" => (Svg(Dunes()), "cover", "no-repeat", "center"),
            _ => ("none", "auto", "no-repeat", "center")
        };
    }

    // Verified against https://atlasuniversalis.com/css/site.yw4zj8iwqx.css on 2026-10-16.
    // These twelve star positions, sizes, and opacities preserve the actual Atlas body surface.
    private const string AtlasStars = "radial-gradient(circle at 8% 14%,rgba(244,248,255,.88) 0 1.2px,transparent 2px),"
        + "radial-gradient(circle at 17% 36%,rgba(228,237,255,.55) 0 1px,transparent 2px),"
        + "radial-gradient(circle at 24% 72%,rgba(237,244,255,.82) 0 1.35px,transparent 2px),"
        + "radial-gradient(circle at 36% 18%,rgba(208,222,245,.48) 0 1px,transparent 2px),"
        + "radial-gradient(circle at 44% 58%,rgba(246,250,255,.92) 0 1.45px,transparent 2px),"
        + "radial-gradient(circle at 51% 81%,rgba(221,233,252,.62) 0 1px,transparent 2px),"
        + "radial-gradient(circle at 62% 29%,rgba(240,246,255,.86) 0 1.25px,transparent 2px),"
        + "radial-gradient(circle at 74% 63%,rgba(211,225,247,.52) 0 1px,transparent 2px),"
        + "radial-gradient(circle at 83% 24%,rgba(245,249,255,.9) 0 1.4px,transparent 2px),"
        + "radial-gradient(circle at 91% 76%,rgba(230,239,255,.68) 0 1.1px,transparent 2px),"
        + "radial-gradient(circle at 67% 9%,rgba(255,255,255,.94) 0 1.6px,transparent 2.4px),"
        + "radial-gradient(circle at 29% 92%,rgba(217,229,250,.56) 0 1px,transparent 2px)";

    private const string QuietStars = "radial-gradient(circle at 8% 14%,rgba(244,248,255,.54) 0 .8px,transparent 1.6px),"
        + "radial-gradient(circle at 24% 72%,rgba(237,244,255,.42) 0 .7px,transparent 1.5px),"
        + "radial-gradient(circle at 44% 58%,rgba(246,250,255,.62) 0 1px,transparent 1.8px),"
        + "radial-gradient(circle at 62% 29%,rgba(240,246,255,.44) 0 .7px,transparent 1.5px),"
        + "radial-gradient(circle at 83% 24%,rgba(245,249,255,.56) 0 .9px,transparent 1.7px),"
        + "radial-gradient(circle at 91% 76%,rgba(230,239,255,.4) 0 .7px,transparent 1.5px),"
        + "radial-gradient(circle at 67% 9%,rgba(255,255,255,.65) 0 1px,transparent 1.8px)";

    private static readonly Dictionary<string, string> SvgCache = new(StringComparer.Ordinal);
    private static string Svg(string content)
    {
        lock (SvgCache)
        {
            if (!SvgCache.TryGetValue(content, out var value)) SvgCache[content] = value = "url(\"data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(content)) + "\")";
            return value;
        }
    }

    private static string StarTile(int width, int height, int count, uint seed, double opacity)
    {
        // Group tiny round line-caps into compact paths. Saved appearance values have a 4 KiB
        // boundary; the actual web design must remain portable without enlarging that contract.
        var paths = Enumerable.Range(0, 6).Select(_ => new StringBuilder()).ToArray();
        var svg = new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' width='" + width + "' height='" + height + "' fill='none' stroke-linecap='round'>");
        double Next() { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return seed / (double)uint.MaxValue; }
        for (var i = 0; i < count; i++)
        {
            var x = (int)(4 + Next() * (width - 8)); var y = (int)(4 + Next() * (height - 8));
            paths[(i % 7 == 0 ? 3 : 0) + i % 3].Append('M').Append(x).Append(' ').Append(y).Append("h.01");
        }
        for (var i = 0; i < paths.Length; i++)
            svg.Append("<path d='").Append(paths[i]).Append("' stroke='").Append(i >= 3 ? "#c0d7ff" : "#edf5ff")
                .Append("' stroke-opacity='").Append(Number(opacity * (i % 3 == 0 ? .48 : i % 3 == 1 ? .7 : .94)))
                .Append("' stroke-width='").Append(i % 3 == 0 ? ".8" : i % 3 == 1 ? "1.15" : "1.45").Append("'/>");
        return svg.Append("</svg>").ToString();
    }

    private static string PaperGrain()
    {
        var paths = new[] { new StringBuilder(), new StringBuilder() };
        uint seed = 96173;
        for (var i = 0; i < 96; i++)
        {
            seed = unchecked(seed * 1664525 + 1013904223); var x = seed % 128;
            seed = unchecked(seed * 1664525 + 1013904223); var y = seed % 128;
            paths[i % 3 == 0 ? 0 : 1].Append('M').Append(x).Append(' ').Append(y).Append("h.01");
        }
        return "<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128' fill='none' stroke-linecap='round'>"
            + "<path d='" + paths[0] + "' stroke='#ffffff' stroke-opacity='.34' stroke-width='.7'/>"
            + "<path d='" + paths[1] + "' stroke='#594b35' stroke-opacity='.075' stroke-width='.7'/></svg>";
    }

    private static string LinenWeave()
    {
        return "<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4' fill='none'>"
            + "<path d='M0 0H4M0 0V4' stroke='#ffffff' stroke-opacity='.27' stroke-width='.55'/>"
            + "<path d='M0 1H4M1 0V4' stroke='#40514a' stroke-opacity='.045' stroke-width='.45'/></svg>";
    }

    private static string Contours()
    {
        var svg = new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' width='1200' height='800' viewBox='0 0 1200 800' fill='none' stroke='#7a756c' stroke-opacity='.13' stroke-width='.8'>");
        for (var i = 0; i < 18; i++)
        {
            var y = -120 + i * 52;
            svg.Append("<path d='M-100 ").Append(y).Append(" C170 ").Append(y + 160).Append(" 200 ").Append(y - 100)
                .Append(" 460 ").Append(y + 35).Append(" S780 ").Append(y + 250).Append(" 1300 ").Append(y - 25)
                .Append("'/>");
        }
        return svg.Append("</svg>").ToString();
    }

    private static string Dunes() => "<svg xmlns='http://www.w3.org/2000/svg' width='1200' height='800' viewBox='0 0 1200 800'>"
        + "<path d='M-100 450C200 230 470 610 810 390S1180 190 1350 350V900H-100Z' fill='#ffffff' fill-opacity='.19'/>"
        + "<path d='M-100 610C210 330 600 670 920 500S1230 390 1350 490V900H-100Z' fill='#8c704b' fill-opacity='.07'/>"
        + "<path d='M-100 720C160 520 500 700 790 640S1140 480 1350 570V900H-100Z' fill='#ffffff' fill-opacity='.22'/>"
        + "<path d='M-100 450C200 230 470 610 810 390S1180 190 1350 350' fill='none' stroke='#ffffff' stroke-opacity='.3' stroke-width='1'/></svg>";
}
