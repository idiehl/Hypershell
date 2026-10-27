using System.Text.RegularExpressions;
using Hypershell.Models;

namespace Hypershell.Services;

public sealed record VisualPageWebTemplateCaptureRequest(
    string SourceUrl,
    string TemplateName,
    string Description,
    string Scope,
    string CategoryGroup,
    string Category);

public sealed record VisualPageWebTemplateCaptureResult(
    VisualPageTemplateDocument Template,
    string RelativePath,
    int ElementCount,
    int RemovedActiveBlocks,
    int RemovedUnsupportedNodes,
    int RemovedExternalReferences,
    IReadOnlyList<VisualPageDiagnostic> ImportDiagnostics);

/// <summary>
/// Converts a fetched HTML document into a bounded, inert Hyperpage template.
/// It intentionally captures semantic structure rather than executable code,
/// external assets, site copy, or a site's CSS identity.
/// </summary>
public sealed partial class VisualPageWebTemplateCaptureService
{
    public const int MaximumCapturedElements = 1200;
    public const string StructureOnlyPolicy =
        "Structure only; placeholder copy; no scripts, remote assets, or captured CSS.";

    private static readonly HashSet<string> RemovedMarkupNames = new(
        [
            "script", "style", "link", "meta", "base", "iframe",
            "object", "embed", "canvas", "svg", "portal", "fencedframe"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> RemovedAttributeNames = new(
        [
            "class", "style", "srcset", "imagesrcset", "integrity",
            "crossorigin", "referrerpolicy", "ping", "action", "formaction",
            "manifest", "nonce", "srcdoc"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ExternalReferenceAttributes = new(
        [
            "src", "poster", "data", "href", "xlink:href", "cite",
            "background", "longdesc", "usemap"
        ],
        StringComparer.OrdinalIgnoreCase);

    private readonly VisualPageComponentCatalog _catalog;
    private readonly VisualPageImportService _importService;

    public VisualPageWebTemplateCaptureService(
        VisualPageComponentCatalog catalog)
    {
        _catalog = catalog;
        _importService = new VisualPageImportService(catalog);
    }

    public VisualPageWebTemplateCaptureResult Capture(
        string html,
        VisualPageWebTemplateCaptureRequest request,
        DateTimeOffset capturedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        if (html.Length > VisualPageImportService.MaximumImportCharacters)
        {
            throw new VisualPageValidationException(
                "Website template capture is limited to 512 KB of HTML.");
        }

        var activeBlocks = ActiveBlockRegex().Matches(html).Count;
        var sanitized = ActiveBlockRegex().Replace(html, string.Empty);
        var body = BodyRegex().Match(sanitized);
        sanitized = body.Success
            ? body.Groups["content"].Value
            : HeadRegex().Replace(sanitized, string.Empty);
        sanitized = DocumentNoiseRegex().Replace(sanitized, string.Empty);

        var imported = _importService.Import(sanitized, razor: false);
        var unsupportedNodes = 0;
        var externalReferences = 0;
        SanitizeTree(
            imported.Root,
            ref unsupportedNodes,
            ref externalReferences);

        var importedChildren = imported.Root.Children
            .Where(child => !ShouldRemove(child))
            .ToList();
        if (importedChildren.Count == 0)
        {
            throw new VisualPageValidationException(
                "The fetched page did not contain supported, visible HTML structure to capture.");
        }

        var root = request.Scope switch
        {
            "page" => Wrapper(
                "core.page",
                request.TemplateName,
                importedChildren,
                new VisualPageLayoutSpec
                {
                    Display = "flex",
                    FlexDirection = "column",
                    Gap = "1rem"
                }),
            "section" => Wrapper(
                "core.section",
                request.TemplateName,
                importedChildren,
                new VisualPageLayoutSpec
                {
                    Display = "flex",
                    FlexDirection = "column",
                    Gap = "1rem",
                    Padding = "clamp(1rem, 4vw, 3rem)"
                }),
            _ => Wrapper(
                "core.container",
                request.TemplateName,
                importedChildren,
                new VisualPageLayoutSpec
                {
                    Display = "flex",
                    FlexDirection = "column",
                    Gap = "0.75rem"
                })
        };

        var elementCount = Flatten(root).Take(MaximumCapturedElements + 1).Count();
        if (elementCount > MaximumCapturedElements)
        {
            throw new VisualPageValidationException(
                $"The captured structure exceeds the {MaximumCapturedElements:N0}-element website template limit.");
        }

        var templateId = Guid.NewGuid().ToString("N");
        var template = new VisualPageTemplateDocument
        {
            TemplateId = templateId,
            Name = request.TemplateName.Trim(),
            Description = request.Description.Trim(),
            Scope = request.Scope,
            StorageScope = "project",
            Library = "This project",
            CategoryGroup = request.CategoryGroup.Trim(),
            Category = request.Category.Trim(),
            UsageHint = request.Scope == "page"
                ? "Create a new page from this reviewed website-derived structure, then add your own copy, assets, and styles."
                : "Insert this reviewed website-derived structure, then add your own copy, assets, and styles.",
            Tags =
            [
                "website capture", "structure only", request.Scope,
                request.CategoryGroup.Trim().ToLowerInvariant()
            ],
            SuggestedPageName = request.Scope == "page"
                ? request.TemplateName.Trim()
                : string.Empty,
            SuggestedPageRoute = request.Scope == "page"
                ? $"/{VisualPageProjectService.MakeSlug(request.TemplateName)}"
                : string.Empty,
            SuggestedPageTitle = request.Scope == "page"
                ? request.TemplateName.Trim()
                : string.Empty,
            SuggestedPageDescription = request.Scope == "page"
                ? request.Description.Trim()
                : string.Empty,
            SourceKind = "website-structure-capture",
            SourceUrl = NormalizeSourceUrl(request.SourceUrl),
            SourceCapturedAtUtc = capturedAtUtc.ToUniversalTime(),
            ContentPolicy = StructureOnlyPolicy,
            Root = root
        };
        var relativePath =
            $"Templates/{VisualPageProjectService.MakeSlug(template.Name)}-{templateId[..8]}.visual-page-template.json";
        return new VisualPageWebTemplateCaptureResult(
            template,
            relativePath,
            elementCount,
            activeBlocks,
            unsupportedNodes,
            externalReferences,
            imported.Diagnostics);
    }

    private void SanitizeTree(
        VisualPageElement parent,
        ref int unsupportedNodes,
        ref int externalReferences)
    {
        for (var index = parent.Children.Count - 1; index >= 0; index--)
        {
            var child = parent.Children[index];
            if (ShouldRemove(child))
            {
                parent.Children.RemoveAt(index);
                unsupportedNodes++;
                continue;
            }
            SanitizeElement(child, ref externalReferences);
            SanitizeTree(child, ref unsupportedNodes, ref externalReferences);
        }
    }

    private void SanitizeElement(
        VisualPageElement element,
        ref int externalReferences)
    {
        foreach (var attribute in element.Attributes.ToList())
        {
            if (attribute.Key.StartsWith("on", StringComparison.OrdinalIgnoreCase)
                || attribute.Key.StartsWith("data-", StringComparison.OrdinalIgnoreCase)
                || RemovedAttributeNames.Contains(attribute.Key))
            {
                element.Attributes.Remove(attribute.Key);
                continue;
            }
            if (attribute.Key.Equals("alt", StringComparison.OrdinalIgnoreCase))
            {
                element.Attributes[attribute.Key] =
                    "Replace with descriptive alternative text.";
                continue;
            }
            if (attribute.Key.Equals(
                    "aria-label",
                    StringComparison.OrdinalIgnoreCase))
            {
                element.Attributes[attribute.Key] =
                    $"Captured {_catalog.GetRequired(element.DescriptorId).MarkupName} structure";
                continue;
            }
            if (attribute.Key.Equals("placeholder", StringComparison.OrdinalIgnoreCase))
            {
                element.Attributes[attribute.Key] = "Enter a value";
                continue;
            }
            if (attribute.Key.Equals("value", StringComparison.OrdinalIgnoreCase)
                || attribute.Key.Equals("title", StringComparison.OrdinalIgnoreCase)
                || attribute.Key.Equals(
                    "aria-description",
                    StringComparison.OrdinalIgnoreCase))
            {
                element.Attributes.Remove(attribute.Key);
                continue;
            }
            if (!ExternalReferenceAttributes.Contains(attribute.Key))
            {
                continue;
            }

            externalReferences++;
            if (attribute.Key.Equals("href", StringComparison.OrdinalIgnoreCase))
            {
                element.Attributes[attribute.Key] = "#";
            }
            else if (attribute.Key.Equals("src", StringComparison.OrdinalIgnoreCase)
                     && _catalog.GetRequired(element.DescriptorId).MarkupName.Equals(
                         "img",
                         StringComparison.OrdinalIgnoreCase))
            {
                element.Attributes[attribute.Key] = "assets/placeholder.svg";
            }
            else
            {
                element.Attributes.Remove(attribute.Key);
            }
        }

        element.Styles.Clear();
        ReplaceTextWithPlaceholder(element);
    }

    private bool ShouldRemove(VisualPageElement element)
    {
        if (element.IsCustomCode)
        {
            return true;
        }
        return RemovedMarkupNames.Contains(
            _catalog.GetRequired(element.DescriptorId).MarkupName);
    }

    private void ReplaceTextWithPlaceholder(VisualPageElement element)
    {
        if (string.IsNullOrWhiteSpace(element.Text))
        {
            return;
        }

        var markup = _catalog.GetRequired(element.DescriptorId).MarkupName;
        element.Text = markup.ToLowerInvariant() switch
        {
            "h1" => "Page heading",
            "h2" or "h3" or "h4" or "h5" or "h6" => "Section heading",
            "p" => "Add supporting content here.",
            "a" => "Link",
            "button" => "Action",
            "label" => "Field label",
            "li" => "List item",
            "blockquote" => "Add an approved quotation.",
            "figcaption" or "caption" => "Add a useful caption.",
            "summary" => "Common question",
            "option" => "Option",
            "code" or "pre" => "Add code or preformatted text.",
            _ => "Content"
        };
    }

    private static VisualPageElement Wrapper(
        string descriptorId,
        string name,
        List<VisualPageElement> children,
        VisualPageLayoutSpec layout) => new()
        {
            DescriptorId = descriptorId,
            Name = name.Trim(),
            Layout = new Dictionary<VisualPageBreakpoint, VisualPageLayoutSpec>
            {
                [VisualPageBreakpoint.Base] = layout
            },
            Children = children
        };

    private static IEnumerable<VisualPageElement> Flatten(
        VisualPageElement root)
    {
        yield return root;
        foreach (var child in root.Children)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }

    private static void ValidateRequest(
        VisualPageWebTemplateCaptureRequest request)
    {
        if (request.Scope is not ("page" or "section" or "element")
            || string.IsNullOrWhiteSpace(request.TemplateName)
            || request.TemplateName.Trim().Length > 200
            || string.IsNullOrWhiteSpace(request.Description)
            || request.Description.Trim().Length > 800
            || string.IsNullOrWhiteSpace(request.CategoryGroup)
            || request.CategoryGroup.Trim().Length > 80
            || string.IsNullOrWhiteSpace(request.Category)
            || request.Category.Trim().Length > 80)
        {
            throw new VisualPageValidationException(
                "Website template metadata is incomplete or exceeds its bounded limits.");
        }
        _ = NormalizeSourceUrl(request.SourceUrl);
    }

    private static string NormalizeSourceUrl(string sourceUrl)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || !string.IsNullOrWhiteSpace(uri.UserInfo)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new VisualPageValidationException(
                "Website templates accept only HTTPS source URLs without credentials or custom ports.");
        }
        return uri.GetLeftPart(UriPartial.Path);
    }

    [GeneratedRegex(
        "<(?:script|style|noscript|template|iframe|object|embed|canvas|svg|portal|fencedframe)\\b[^>]*>[\\s\\S]*?</(?:script|style|noscript|template|iframe|object|embed|canvas|svg|portal|fencedframe)\\s*>|<(?:script|style|link|meta|base|iframe|object|embed|canvas|svg|portal|fencedframe)\\b[^>]*?/?>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ActiveBlockRegex();

    [GeneratedRegex(
        "<body\\b[^>]*>(?<content>[\\s\\S]*?)</body\\s*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BodyRegex();

    [GeneratedRegex(
        "<head\\b[^>]*>[\\s\\S]*?</head\\s*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeadRegex();

    [GeneratedRegex(
        "<!doctype[^>]*>|<\\?xml[^>]*\\?>|<!--([\\s\\S]*?)-->|</?(?:html|body)\\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DocumentNoiseRegex();
}
