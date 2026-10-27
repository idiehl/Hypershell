using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using WordDocument = DocumentFormat.OpenXml.Wordprocessing.Document;
using WordText = DocumentFormat.OpenXml.Wordprocessing.Text;

namespace Hypershell.Services;

internal enum StructuredDocumentBlockKind
{
    Paragraph,
    Heading,
    BulletListItem,
    NumberedListItem,
    Code,
    Quote,
    HorizontalRule
}

internal sealed record StructuredDocumentBlock(
    StructuredDocumentBlockKind Kind,
    string Text,
    int Level = 0,
    int Offset = 0,
    int Length = 0);

internal static partial class StructuredDocumentParser
{
    [GeneratedRegex("^(#{1,6})[ \\t]+(.+?)[ \\t]*#*[ \\t]*$", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex("^[ \\t]*[-+*][ \\t]+(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex BulletPattern();

    [GeneratedRegex("^[ \\t]*[0-9]+[.)][ \\t]+(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberedPattern();

    [GeneratedRegex("^[ \\t]*>[ \\t]?(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex QuotePattern();

    [GeneratedRegex("^[ \\t]*(```|~~~)(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex FencePattern();

    [GeneratedRegex("^[ \\t]*((-[ \\t]*){3,}|(_[ \\t]*){3,}|(\\*[ \\t]*){3,})$", RegexOptions.CultureInvariant)]
    private static partial Regex HorizontalRulePattern();

    [GeneratedRegex("!\\[([^]]*)\\]\\([^)]*\\)", RegexOptions.CultureInvariant)]
    private static partial Regex ImagePattern();

    [GeneratedRegex("\\[([^]]+)\\]\\(([^)]*)\\)", RegexOptions.CultureInvariant)]
    private static partial Regex LinkPattern();

    [GeneratedRegex("(\\*\\*|__|~~|`|\\*|_)", RegexOptions.CultureInvariant)]
    private static partial Regex InlineMarkerPattern();

    public static IReadOnlyList<StructuredDocumentBlock> ParseMarkdown(string content)
    {
        var lines = SplitLines(content);
        var blocks = new List<StructuredDocumentBlock>();
        var paragraphLines = new List<(string Text, int Offset, int Length)>();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var fence = FencePattern().Match(line.Text);
            if (fence.Success)
            {
                FlushParagraph(paragraphLines, blocks);
                var marker = fence.Groups[1].Value;
                var start = line.Offset;
                var code = new StringBuilder();
                var end = line.Offset + line.Length;
                index++;
                while (index < lines.Count)
                {
                    var candidate = lines[index];
                    end = candidate.Offset + candidate.Length;
                    if (candidate.Text.TrimStart().StartsWith(marker, StringComparison.Ordinal))
                    {
                        break;
                    }

                    if (code.Length > 0)
                    {
                        code.Append('\n');
                    }

                    code.Append(candidate.Text);
                    index++;
                }

                blocks.Add(new StructuredDocumentBlock(
                    StructuredDocumentBlockKind.Code,
                    code.ToString(),
                    Offset: start,
                    Length: Math.Max(0, end - start)));
                continue;
            }

            if (string.IsNullOrWhiteSpace(line.Text))
            {
                FlushParagraph(paragraphLines, blocks);
                continue;
            }

            var heading = HeadingPattern().Match(line.Text);
            if (heading.Success)
            {
                FlushParagraph(paragraphLines, blocks);
                blocks.Add(new StructuredDocumentBlock(
                    StructuredDocumentBlockKind.Heading,
                    CleanInlineMarkdown(heading.Groups[2].Value),
                    heading.Groups[1].Length,
                    line.Offset,
                    line.Length));
                continue;
            }

            var bullet = BulletPattern().Match(line.Text);
            if (bullet.Success)
            {
                FlushParagraph(paragraphLines, blocks);
                blocks.Add(new StructuredDocumentBlock(
                    StructuredDocumentBlockKind.BulletListItem,
                    CleanInlineMarkdown(bullet.Groups[1].Value),
                    Offset: line.Offset,
                    Length: line.Length));
                continue;
            }

            var numbered = NumberedPattern().Match(line.Text);
            if (numbered.Success)
            {
                FlushParagraph(paragraphLines, blocks);
                blocks.Add(new StructuredDocumentBlock(
                    StructuredDocumentBlockKind.NumberedListItem,
                    CleanInlineMarkdown(numbered.Groups[1].Value),
                    Offset: line.Offset,
                    Length: line.Length));
                continue;
            }

            var quote = QuotePattern().Match(line.Text);
            if (quote.Success)
            {
                FlushParagraph(paragraphLines, blocks);
                blocks.Add(new StructuredDocumentBlock(
                    StructuredDocumentBlockKind.Quote,
                    CleanInlineMarkdown(quote.Groups[1].Value),
                    Offset: line.Offset,
                    Length: line.Length));
                continue;
            }

            if (HorizontalRulePattern().IsMatch(line.Text))
            {
                FlushParagraph(paragraphLines, blocks);
                blocks.Add(new StructuredDocumentBlock(
                    StructuredDocumentBlockKind.HorizontalRule,
                    string.Empty,
                    Offset: line.Offset,
                    Length: line.Length));
                continue;
            }

            paragraphLines.Add(line);
        }

        FlushParagraph(paragraphLines, blocks);
        return blocks;
    }

    public static IReadOnlyList<StructuredDocumentBlock> ParsePlainText(string content)
    {
        var paragraphs = Regex.Split(content, "(?:\\r\\n|\\r|\\n){2,}")
            .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph))
            .Select(paragraph => new StructuredDocumentBlock(
                StructuredDocumentBlockKind.Paragraph,
                paragraph.Trim()))
            .ToList();
        return paragraphs.Count > 0
            ? paragraphs
            : [new StructuredDocumentBlock(StructuredDocumentBlockKind.Paragraph, content)];
    }

    public static string CleanInlineMarkdown(string value)
    {
        var result = ImagePattern().Replace(value, match => match.Groups[1].Value);
        result = LinkPattern().Replace(result, match =>
        {
            var label = match.Groups[1].Value;
            var target = match.Groups[2].Value;
            return string.IsNullOrWhiteSpace(target) ? label : $"{label} ({target})";
        });
        result = InlineMarkerPattern().Replace(result, string.Empty);
        result = result.Replace("\\\\", "\\", StringComparison.Ordinal);
        return WebUtility.HtmlDecode(result).Trim();
    }

    private static void FlushParagraph(
        ICollection<(string Text, int Offset, int Length)> lines,
        ICollection<StructuredDocumentBlock> blocks)
    {
        if (lines.Count == 0)
        {
            return;
        }

        var first = lines.First();
        var last = lines.Last();
        blocks.Add(new StructuredDocumentBlock(
            StructuredDocumentBlockKind.Paragraph,
            CleanInlineMarkdown(string.Join(" ", lines.Select(line => line.Text.Trim()))),
            Offset: first.Offset,
            Length: last.Offset + last.Length - first.Offset));
        lines.Clear();
    }

    private static IReadOnlyList<(string Text, int Offset, int Length)> SplitLines(
        string content)
    {
        var lines = new List<(string Text, int Offset, int Length)>();
        var offset = 0;
        while (offset < content.Length)
        {
            var lineEnd = offset;
            while (lineEnd < content.Length && content[lineEnd] is not '\r' and not '\n')
            {
                lineEnd++;
            }

            var terminatorLength = lineEnd < content.Length
                ? content[lineEnd] == '\r'
                  && lineEnd + 1 < content.Length
                  && content[lineEnd + 1] == '\n'
                    ? 2
                    : 1
                : 0;
            lines.Add((
                content[offset..lineEnd],
                offset,
                lineEnd + terminatorLength - offset));
            offset = lineEnd + terminatorLength;
        }

        if (content.Length == 0 || content[^1] is '\r' or '\n')
        {
            lines.Add((string.Empty, content.Length, 0));
        }

        return lines;
    }
}

internal static partial class StandaloneHtmlDocumentBuilder
{
    private static readonly MarkdownPipeline MarkdownPipeline =
        new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .DisableHtml()
            .Build();

    [GeneratedRegex("<img\\b[^>]*\\balt=\"([^\"]*)\"[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImageTagPattern();

    [GeneratedRegex("href=\"([^\"]*)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkAttributePattern();

    public static byte[] FromMarkdown(string title, string content)
    {
        var body = Markdig.Markdown.ToHtml(content, MarkdownPipeline);
        body = ImageTagPattern().Replace(
            body,
            match => $"<span class=\"image-placeholder\">[Image: {match.Groups[1].Value}]</span>");
        body = LinkAttributePattern().Replace(body, match =>
        {
            var target = WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
            return IsSafeLink(target)
                ? $"href=\"{HtmlEncoder.Default.Encode(target)}\" rel=\"noreferrer noopener\""
                : "href=\"#\"";
        });
        return CreateDocument(title, body);
    }

    public static byte[] FromPreformatted(
        string title,
        string content,
        string languageLabel)
    {
        var body = $"<div class=\"format-label\">{HtmlEncoder.Default.Encode(languageLabel)}</div>"
                   + $"<pre><code>{HtmlEncoder.Default.Encode(content)}</code></pre>";
        return CreateDocument(title, body);
    }

    private static byte[] CreateDocument(string title, string body)
    {
        var safeTitle = HtmlEncoder.Default.Encode(title);
        var html = $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta name="referrer" content="no-referrer">
              <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'none'; style-src 'unsafe-inline'; img-src data:; connect-src 'none'; object-src 'none'; frame-src 'none'; media-src 'none'; form-action 'none'; base-uri 'none'">
              <title>{{safeTitle}}</title>
              <style>
                :root { color-scheme: light dark; }
                body { max-width: 880px; margin: 48px auto; padding: 0 28px 72px; font: 16px/1.65 system-ui, sans-serif; color: #1f2328; background: #fff; }
                h1, h2, h3, h4, h5, h6 { line-height: 1.25; margin-top: 1.5em; }
                h1, h2 { border-bottom: 1px solid #d0d7de; padding-bottom: .3em; }
                pre, code { font-family: Consolas, "Cascadia Code", monospace; }
                pre { overflow: auto; padding: 16px; border: 1px solid #d0d7de; border-radius: 6px; background: #f6f8fa; white-space: pre-wrap; }
                code { padding: .15em .35em; border-radius: 4px; background: #f0f2f4; }
                pre code { padding: 0; background: transparent; }
                blockquote { margin-left: 0; padding-left: 1em; color: #57606a; border-left: 4px solid #d0d7de; }
                table { border-collapse: collapse; max-width: 100%; }
                th, td { border: 1px solid #d0d7de; padding: 6px 10px; }
                a { color: #0969da; }
                .format-label, .image-placeholder { color: #57606a; font-size: 12px; }
                @media (prefers-color-scheme: dark) {
                  body { color: #e6edf3; background: #0d1117; }
                  h1, h2, pre, th, td { border-color: #30363d; }
                  pre, code { background: #161b22; }
                  pre code { background: transparent; }
                  blockquote, .format-label, .image-placeholder { color: #8b949e; }
                  a { color: #58a6ff; }
                }
                @media print { body { max-width: none; margin: 0; color: #000; background: #fff; } }
              </style>
            </head>
            <body>
            {{body}}
            </body>
            </html>
            """;
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(html);
    }

    private static bool IsSafeLink(string target)
    {
        if (string.IsNullOrWhiteSpace(target) || target.StartsWith('#'))
        {
            return true;
        }

        return Uri.TryCreate(target, UriKind.Absolute, out var uri)
               && uri.Scheme is "http" or "https" or "mailto";
    }
}

internal static class WordprocessingDocumentExporter
{
    public static byte[] Create(
        string title,
        IReadOnlyList<StructuredDocumentBlock> blocks)
    {
        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(
                   stream,
                   WordprocessingDocumentType.Document,
                   autoSave: true))
        {
            package.PackageProperties.Title = title;
            package.PackageProperties.Creator = "Hypershell";
            package.PackageProperties.Created = DateTime.UtcNow;

            var mainPart = package.AddMainDocumentPart();
            AddStyles(mainPart);
            AddNumbering(mainPart);
            var body = new Body();
            foreach (var block in blocks)
            {
                body.Append(CreateParagraph(block));
            }

            body.Append(new SectionProperties(
                new PageSize { Width = 12240, Height = 15840 },
                new PageMargin
                {
                    Top = 1080,
                    Right = 1080,
                    Bottom = 1080,
                    Left = 1080,
                    Header = 720,
                    Footer = 720,
                    Gutter = 0
                }));
            mainPart.Document = new WordDocument(body);
            mainPart.Document.Save();

            var validationErrors = new OpenXmlValidator()
                .Validate(package)
                .Take(5)
                .Select(error => error.Description)
                .ToList();
            if (validationErrors.Count > 0)
            {
                throw new InvalidDataException(
                    "The generated Word package did not pass Open XML validation: "
                    + string.Join("; ", validationErrors));
            }
        }

        return stream.ToArray();
    }

    private static Paragraph CreateParagraph(StructuredDocumentBlock block)
    {
        var properties = block.Kind switch
        {
            StructuredDocumentBlockKind.Heading =>
                new ParagraphProperties(new ParagraphStyleId
                {
                    Val = $"Heading{Math.Clamp(block.Level, 1, 6)}"
                }),
            StructuredDocumentBlockKind.BulletListItem =>
                ListProperties(numberingId: 1),
            StructuredDocumentBlockKind.NumberedListItem =>
                ListProperties(numberingId: 2),
            StructuredDocumentBlockKind.Code =>
                new ParagraphProperties(new ParagraphStyleId { Val = "CodeBlock" }),
            StructuredDocumentBlockKind.Quote =>
                new ParagraphProperties(new ParagraphStyleId { Val = "Quote" }),
            StructuredDocumentBlockKind.HorizontalRule =>
                new ParagraphProperties(new ParagraphBorders(
                    new BottomBorder
                    {
                        Val = BorderValues.Single,
                        Size = 6,
                        Space = 1,
                        Color = "A8AFB8"
                    })),
            _ => new ParagraphProperties(new ParagraphStyleId { Val = "Normal" })
        };
        var paragraph = new Paragraph(properties);
        if (block.Kind != StructuredDocumentBlockKind.HorizontalRule)
        {
            paragraph.Append(CreateTextRun(
                block.Text,
                monospace: block.Kind == StructuredDocumentBlockKind.Code));
        }

        return paragraph;
    }

    private static ParagraphProperties ListProperties(int numberingId) =>
        new(
            new ParagraphStyleId { Val = "ListParagraph" },
            new NumberingProperties(
                new NumberingLevelReference { Val = 0 },
                new NumberingId { Val = numberingId }));

    private static Run CreateTextRun(string value, bool monospace)
    {
        var run = new Run();
        if (monospace)
        {
            run.Append(new RunProperties(
                new RunFonts
                {
                    Ascii = "Cascadia Mono",
                    HighAnsi = "Cascadia Mono",
                    EastAsia = "Cascadia Mono"
                },
                new FontSize { Val = "19" }));
        }

        var lines = Regex.Split(value, "\\r\\n|\\r|\\n");
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                run.Append(new Break());
            }

            run.Append(new WordText(lines[index])
            {
                Space = SpaceProcessingModeValues.Preserve
            });
        }

        return run;
    }

    private static void AddStyles(MainDocumentPart mainPart)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        var styles = new Styles();
        styles.Append(CreateParagraphStyle("Normal", "Normal", 22, "Aptos"));
        for (var level = 1; level <= 6; level++)
        {
            styles.Append(CreateHeadingStyle(level));
        }

        styles.Append(CreateParagraphStyle(
            "ListParagraph",
            "List Paragraph",
            22,
            "Aptos",
            leftIndent: 720));
        styles.Append(CreateParagraphStyle(
            "CodeBlock",
            "Code Block",
            19,
            "Cascadia Mono",
            before: 120,
            after: 120,
            shading: "F1F3F5"));
        styles.Append(CreateParagraphStyle(
            "Quote",
            "Quote",
            22,
            "Aptos",
            leftIndent: 540,
            color: "57606A"));
        stylesPart.Styles = styles;
        stylesPart.Styles.Save();
    }

    private static Style CreateParagraphStyle(
        string id,
        string name,
        int fontSize,
        string fontFamily,
        int before = 0,
        int after = 120,
        int leftIndent = 0,
        string color = "1F2328",
        string? shading = null,
        bool bold = false)
    {
        var paragraphProperties = new StyleParagraphProperties();
        if (!string.IsNullOrWhiteSpace(shading))
        {
            paragraphProperties.Append(new Shading
            {
                Val = ShadingPatternValues.Clear,
                Fill = shading,
                Color = "auto"
            });
        }

        paragraphProperties.Append(new SpacingBetweenLines
        {
            Before = before.ToString(),
            After = after.ToString(),
            Line = "300",
            LineRule = LineSpacingRuleValues.Auto
        });
        if (leftIndent > 0)
        {
            paragraphProperties.Append(new Indentation
            {
                Left = leftIndent.ToString()
            });
        }

        var runProperties = new StyleRunProperties(
            new RunFonts
            {
                Ascii = fontFamily,
                HighAnsi = fontFamily,
                EastAsia = fontFamily
            });
        if (bold)
        {
            runProperties.Append(new Bold());
        }

        runProperties.Append(
            new Color { Val = color },
            new FontSize { Val = fontSize.ToString() },
            new FontSizeComplexScript { Val = fontSize.ToString() });

        return new Style(
            new StyleName { Val = name },
            paragraphProperties,
            runProperties)
        {
            Type = StyleValues.Paragraph,
            StyleId = id,
            Default = id == "Normal"
        };
    }

    private static Style CreateHeadingStyle(int level)
    {
        var sizes = new[] { 34, 30, 27, 24, 22, 21 };
        var style = CreateParagraphStyle(
            $"Heading{level}",
            $"Heading {level}",
            sizes[level - 1],
            "Aptos Display",
            before: level <= 2 ? 280 : 220,
            after: 100,
            color: "17212B",
            bold: true);
        var paragraphProperties = style.StyleParagraphProperties
                                  ?? throw new InvalidDataException(
                                      "The generated heading style has no paragraph properties.");
        style.InsertBefore(new BasedOn { Val = "Normal" }, paragraphProperties);
        style.InsertBefore(new NextParagraphStyle { Val = "Normal" }, paragraphProperties);
        style.InsertBefore(new PrimaryStyle(), paragraphProperties);
        return style;
    }

    private static void AddNumbering(MainDocumentPart mainPart)
    {
        var numberingPart = mainPart.AddNewPart<NumberingDefinitionsPart>();
        var bullet = new AbstractNum(
            new MultiLevelType { Val = MultiLevelValues.SingleLevel },
            new Level(
                new NumberingFormat { Val = NumberFormatValues.Bullet },
                new LevelText { Val = "•" },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(new Indentation
                {
                    Left = "720",
                    Hanging = "360"
                }))
            { LevelIndex = 0 })
        { AbstractNumberId = 1 };
        var numbered = new AbstractNum(
            new MultiLevelType { Val = MultiLevelValues.SingleLevel },
            new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.Decimal },
                new LevelText { Val = "%1." },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(new Indentation
                {
                    Left = "720",
                    Hanging = "360"
                }))
            { LevelIndex = 0 })
        { AbstractNumberId = 2 };
        numberingPart.Numbering = new Numbering(
            bullet,
            numbered,
            new NumberingInstance(new AbstractNumId { Val = 1 })
            {
                NumberID = 1
            },
            new NumberingInstance(new AbstractNumId { Val = 2 })
            {
                NumberID = 2
            });
        numberingPart.Numbering.Save();
    }
}
