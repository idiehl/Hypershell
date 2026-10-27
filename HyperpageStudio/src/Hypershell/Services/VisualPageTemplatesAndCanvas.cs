using System.IO;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Hypershell.Models;

namespace Hypershell.Services;

public sealed class VisualPageTemplateDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string TemplateId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Template";
    public string Description { get; set; } = string.Empty;
    public string Scope { get; set; } = "element";
    public string StorageScope { get; set; } = "built-in";
    public string Library { get; set; } = "Core essentials";
    public string CategoryGroup { get; set; } = "General";
    public string Category { get; set; } = "General";
    public string UsageHint { get; set; } = string.Empty;
    public string SourceKind { get; set; } = "authored";
    public string SourceUrl { get; set; } = string.Empty;
    public DateTimeOffset? SourceCapturedAtUtc { get; set; }
    public string ContentPolicy { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public string SuggestedPageName { get; set; } = string.Empty;
    public string SuggestedPageRoute { get; set; } = string.Empty;
    public string SuggestedPageTitle { get; set; } = string.Empty;
    public string SuggestedPageDescription { get; set; } = string.Empty;
    public Dictionary<string, string> ThemeTokens { get; set; } =
        new(StringComparer.Ordinal);
    public VisualPageElement Root { get; set; } = new();
    public List<string> AssetIds { get; set; } = [];
    public List<string> Dependencies { get; set; } = [];
    public Dictionary<string, string> PackagedAssets { get; set; } =
        new(StringComparer.Ordinal);

    [JsonIgnore]
    public string SourcePath { get; set; } = string.Empty;

    [JsonIgnore]
    public string ScopeLabel => Scope switch
    {
        "page" => "Page starter",
        "section" => "Section",
        _ => "Reusable element"
    };

    [JsonIgnore]
    public string StorageLabel => StorageScope switch
    {
        "project" => "This project",
        "personal" => "My templates",
        _ => "Built in"
    };

    [JsonIgnore]
    public string CategoryGroupLabel =>
        VisualPageTemplateTaxonomy.ResolveCategoryGroup(this);

    [JsonIgnore]
    public string SearchText => string.Join(
        ' ',
        new[]
        {
            Name, Description, ScopeLabel, StorageLabel, Library,
            CategoryGroupLabel, Category, UsageHint, SourceKind, ContentPolicy
        }.Concat(Tags));
}

public sealed record VisualPageTemplateCatalogLoadResult(
    IReadOnlyList<VisualPageTemplateDocument> Templates,
    int RejectedCount);

public sealed class VisualPageTemplateService
{
    private const int MaximumTemplateBytes = 4 * 1024 * 1024;
    private const int MaximumPersonalTemplates = 256;
    private const int MaximumPackagedAssets = 64;
    private const long MaximumPackagedAssetBytes = 32L * 1024 * 1024;
    private const long MaximumPackagedAssetTotalBytes = 128L * 1024 * 1024;
    private readonly VisualPageComponentCatalog _catalog;

    public VisualPageTemplateService(
        VisualPageComponentCatalog catalog,
        string? personalTemplateRoot = null)
    {
        _catalog = catalog;
        PersonalTemplateRoot = Path.GetFullPath(
            personalTemplateRoot
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Hypershell",
                "VisualPageStudio",
                "Templates"));
    }

    public string PersonalTemplateRoot { get; }

    public IReadOnlyList<VisualPageTemplateDocument> BuiltInTemplates { get; } =
        CreateBuiltIns();

    public async Task<string> SavePersonalAsync(
        VisualPageTemplateDocument template,
        IReadOnlyDictionary<string, string> packagedAssetSources,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(packagedAssetSources);
        if (packagedAssetSources.Count > MaximumPackagedAssets)
        {
            throw new VisualPageValidationException(
                $"Personal templates support at most {MaximumPackagedAssets} packaged assets.");
        }
        Directory.CreateDirectory(PersonalTemplateRoot);
        VisualPageProjectService.EnsureNoReparseTraversal(
            PersonalTemplateRoot,
            PersonalTemplateRoot);
        var fileStem = $"{VisualPageProjectService.MakeSlug(template.Name)}-{SafeIdPrefix(template.TemplateId)}";
        var templatePath = VisualPageProjectService.ResolveProjectPath(
            PersonalTemplateRoot,
            $"{fileStem}.visual-page-template.json");
        var packageDirectory = $"{fileStem}.assets";
        long totalBytes = 0;
        template.StorageScope = "personal";
        template.Library = "My templates";
        template.PackagedAssets.Clear();
        foreach (var asset in packagedAssetSources.OrderBy(
                     value => value.Key,
                     StringComparer.Ordinal))
        {
            if (!IsSafeAssetId(asset.Key))
            {
                throw new VisualPageValidationException(
                    "Personal template assets require stable IDs.");
            }
            var source = new FileInfo(Path.GetFullPath(asset.Value));
            if (!source.Exists
                || source.Length is <= 0 or > MaximumPackagedAssetBytes)
            {
                throw new VisualPageValidationException(
                    "A personal template asset is missing or exceeds the 32 MB per-file limit.");
            }
            VisualPageProjectService.RejectReparsePoint(source.FullName);
            totalBytes += source.Length;
            if (totalBytes > MaximumPackagedAssetTotalBytes)
            {
                throw new VisualPageValidationException(
                    "Personal template assets exceed the 128 MB package limit.");
            }
            var relativePath = $"{packageDirectory}/{asset.Key}{source.Extension.ToLowerInvariant()}";
            var destination = VisualPageProjectService.ResolveProjectPath(
                PersonalTemplateRoot,
                relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await CopyAtomicAsync(source.FullName, destination, cancellationToken);
            template.PackagedAssets[asset.Key] = relativePath;
        }
        await SaveAsync(templatePath, template, cancellationToken);
        template.SourcePath = templatePath;
        return templatePath;
    }

    public async Task<VisualPageTemplateCatalogLoadResult> LoadPersonalAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(PersonalTemplateRoot))
        {
            return new VisualPageTemplateCatalogLoadResult([], 0);
        }
        VisualPageProjectService.EnsureNoReparseTraversal(
            PersonalTemplateRoot,
            PersonalTemplateRoot);
        var paths = Directory.EnumerateFiles(
                PersonalTemplateRoot,
                "*.visual-page-template.json",
                SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumPersonalTemplates + 1)
            .ToList();
        var rejected = paths.Count > MaximumPersonalTemplates ? 1 : 0;
        var templates = new List<VisualPageTemplateDocument>();
        foreach (var path in paths.Take(MaximumPersonalTemplates))
        {
            try
            {
                VisualPageProjectService.RejectReparsePoint(path);
                var template = await LoadAsync(path, cancellationToken);
                template.StorageScope = "personal";
                template.Library = "My templates";
                templates.Add(template);
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException
                or JsonException or VisualPageValidationException)
            {
                rejected++;
            }
        }
        return new VisualPageTemplateCatalogLoadResult(templates, rejected);
    }

    public string ResolvePackagedAssetPath(
        VisualPageTemplateDocument template,
        string assetId)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (string.IsNullOrWhiteSpace(template.SourcePath)
            || !template.PackagedAssets.TryGetValue(assetId, out var relativePath))
        {
            throw new VisualPageValidationException(
                $"Personal template asset '{assetId}' is unavailable.");
        }
        var templateDirectory = Path.GetDirectoryName(
            Path.GetFullPath(template.SourcePath))!;
        var path = VisualPageProjectService.ResolveProjectPath(
            templateDirectory,
            relativePath);
        if (!File.Exists(path))
        {
            throw new VisualPageValidationException(
                $"Personal template asset '{assetId}' is missing.");
        }
        VisualPageProjectService.RejectReparsePoint(path);
        return path;
    }

    public VisualPageElement Instantiate(
        VisualPageTemplateDocument template,
        IReadOnlyDictionary<string, string>? assetRemapping = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        var root = VisualPageJson.Clone(template.Root);
        VisualPageMutationEngine.RemapElementIds(root);
        if (template.Scope != "page")
        {
            RemapDomIds(root);
        }
        if (assetRemapping is not null)
        {
            foreach (var element in Flatten(root))
            {
                foreach (var attribute in element.Attributes.ToList())
                {
                    var value = attribute.Value;
                    foreach (var mapping in assetRemapping)
                    {
                        value = value.Replace(
                            $"asset:{mapping.Key}",
                            mapping.Value,
                            StringComparison.Ordinal);
                    }
                    element.Attributes[attribute.Key] = value;
                }
            }
        }
        return root;
    }

    private static void RemapDomIds(VisualPageElement root)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var mappings = Flatten(root)
            .Select(element => element.Attributes.GetValueOrDefault("id"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                value => value!,
                value => $"{value}-{suffix}",
                StringComparer.Ordinal);
        if (mappings.Count == 0)
        {
            return;
        }
        var tokenReferences = new HashSet<string>(
            [
                "aria-activedescendant", "aria-controls", "aria-describedby", "aria-details",
                "aria-errormessage", "aria-flowto", "aria-labelledby",
                "aria-owns", "headers", "itemref"
            ],
            StringComparer.OrdinalIgnoreCase);
        var singleReferences = new HashSet<string>(
            [
                "commandfor", "for", "form", "list", "popovertarget"
            ],
            StringComparer.OrdinalIgnoreCase);
        foreach (var element in Flatten(root))
        {
            foreach (var attribute in element.Attributes.ToList())
            {
                if (attribute.Key.Equals("id", StringComparison.OrdinalIgnoreCase)
                    && mappings.TryGetValue(attribute.Value, out var remappedId))
                {
                    element.Attributes[attribute.Key] = remappedId;
                    continue;
                }
                if ((attribute.Key.Equals("href", StringComparison.OrdinalIgnoreCase)
                     || attribute.Key.Equals("xlink:href", StringComparison.OrdinalIgnoreCase)
                     || attribute.Key.Equals("usemap", StringComparison.OrdinalIgnoreCase))
                    && attribute.Value.StartsWith('#')
                    && mappings.TryGetValue(attribute.Value[1..], out remappedId))
                {
                    element.Attributes[attribute.Key] = $"#{remappedId}";
                    continue;
                }
                if (singleReferences.Contains(attribute.Key)
                    && mappings.TryGetValue(attribute.Value, out remappedId))
                {
                    element.Attributes[attribute.Key] = remappedId;
                    continue;
                }
                if (!tokenReferences.Contains(attribute.Key))
                {
                    continue;
                }
                element.Attributes[attribute.Key] = string.Join(
                    ' ',
                    attribute.Value.Split(
                            ' ',
                            StringSplitOptions.RemoveEmptyEntries
                            | StringSplitOptions.TrimEntries)
                        .Select(token => mappings.GetValueOrDefault(token, token)));
            }
        }
    }

    public async Task SaveAsync(
        string path,
        VisualPageTemplateDocument template,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        var fullPath = Path.GetFullPath(path);
        ValidateTemplate(template, fullPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(template, VisualPageJson.Options),
                new UTF8Encoding(false),
                cancellationToken);
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public void ValidateForSave(
        string path,
        VisualPageTemplateDocument template)
    {
        ArgumentNullException.ThrowIfNull(template);
        ValidateTemplate(template, Path.GetFullPath(path));
    }

    public async Task<VisualPageTemplateDocument> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(Path.GetFullPath(path));
        if (!info.Exists || info.Length > MaximumTemplateBytes)
        {
            throw new VisualPageValidationException(
                "The template is missing or exceeds the 4 MB limit.");
        }
        VisualPageProjectService.RejectReparsePoint(info.FullName);
        await using var stream = info.OpenRead();
        var template = await JsonSerializer.DeserializeAsync<VisualPageTemplateDocument>(
                           stream,
                           VisualPageJson.Options,
                           cancellationToken)
                       ?? throw new VisualPageValidationException(
                           "The template document is invalid.");
        template.SourcePath = info.FullName;
        ValidateTemplate(template, info.FullName);
        return template;
    }

    private void ValidateTemplate(
        VisualPageTemplateDocument template,
        string sourcePath)
    {
        if (!IsSafeAssetId(template.TemplateId)
            || string.IsNullOrWhiteSpace(template.Name)
            || template.Name.Length > 200)
        {
            throw new VisualPageValidationException(
                "Templates require a stable ID and a bounded name.");
        }
        if (template.Scope is not ("page" or "section" or "element"))
        {
            throw new VisualPageValidationException(
                "Template scope must be page, section, or element.");
        }
        template.AssetIds ??= [];
        template.Dependencies ??= [];
        template.Tags ??= [];
        template.Description ??= string.Empty;
        template.Library ??= string.Empty;
        template.CategoryGroup ??= "General";
        template.Category ??= string.Empty;
        template.UsageHint ??= string.Empty;
        template.SourceKind ??= "authored";
        template.SourceUrl ??= string.Empty;
        template.ContentPolicy ??= string.Empty;
        template.SuggestedPageName ??= string.Empty;
        template.SuggestedPageRoute ??= string.Empty;
        template.SuggestedPageTitle ??= string.Empty;
        template.SuggestedPageDescription ??= string.Empty;
        template.ThemeTokens ??= new Dictionary<string, string>(
            StringComparer.Ordinal);
        template.PackagedAssets ??= new Dictionary<string, string>(
            StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(template.Library)
            || template.Library.Length > 80
            || string.IsNullOrWhiteSpace(template.CategoryGroup)
            || template.CategoryGroup.Length > 80
            || string.IsNullOrWhiteSpace(template.Category)
            || template.Category.Length > 80
            || template.SourceKind.Length > 40
            || template.SourceUrl.Length > 2048
            || template.ContentPolicy.Length > 200
            || template.Description.Length > 800
            || template.UsageHint.Length > 800
            || template.SuggestedPageName.Length > 200
            || template.SuggestedPageRoute.Length > 300
            || template.SuggestedPageTitle.Length > 300
            || template.SuggestedPageDescription.Length > 800
            || template.Tags.Count > 32
            || template.Tags.Any(tag =>
                string.IsNullOrWhiteSpace(tag) || tag.Length > 64)
            || template.ThemeTokens.Count > 64
            || template.ThemeTokens.Any(token =>
                !Regex.IsMatch(
                    token.Key,
                    "^[a-z][a-z0-9.-]{0,79}$",
                    RegexOptions.CultureInvariant)
                || string.IsNullOrWhiteSpace(token.Value)
                || token.Value.Length > 500
                || token.Value.Any(char.IsControl)))
        {
            throw new VisualPageValidationException(
                "The template library metadata is incomplete or exceeds its bounded limit.");
        }
        if (!string.IsNullOrWhiteSpace(template.SourceUrl)
            && (!Uri.TryCreate(
                    template.SourceUrl,
                    UriKind.Absolute,
                    out var sourceUri)
                || sourceUri.Scheme != Uri.UriSchemeHttps
                || !sourceUri.IsDefaultPort
                || !string.IsNullOrWhiteSpace(sourceUri.UserInfo)))
        {
            throw new VisualPageValidationException(
                "Template source metadata accepts only HTTPS URLs without credentials or custom ports.");
        }
        if (template.Root is null
            || template.AssetIds.Count > MaximumPackagedAssets
            || template.AssetIds.Any(assetId => !IsSafeAssetId(assetId))
            || template.Dependencies.Count > 128
            || template.PackagedAssets.Count > MaximumPackagedAssets
            || template.PackagedAssets.Any(value =>
                !template.AssetIds.Contains(value.Key, StringComparer.Ordinal)
                || string.IsNullOrWhiteSpace(value.Value)))
        {
            throw new VisualPageValidationException(
                "The template dependency or asset package is invalid or exceeds its bounded limit.");
        }
        var pending = new Stack<VisualPageElement>();
        pending.Push(template.Root);
        var elementCount = 0;
        while (pending.TryPop(out var element))
        {
            elementCount++;
            if (elementCount > 2000)
            {
                throw new VisualPageValidationException(
                    "Templates support at most 2,000 elements.");
            }
            if (string.IsNullOrWhiteSpace(element.DescriptorId)
                || element.Attributes is null
                || element.Styles is null
                || element.Layout is null
                || element.Bindings is null
                || element.Events is null
                || element.Children is null
                || element.Children.Any(child => child is null))
            {
                throw new VisualPageValidationException(
                    "A template contains an incomplete element node.");
            }
            _catalog.GetRequired(element.DescriptorId);
            foreach (var child in element.Children)
            {
                pending.Push(child);
            }
        }
        if (template.PackagedAssets.Count == 0)
        {
            return;
        }
        var directory = Path.GetDirectoryName(sourcePath)!;
        long totalBytes = 0;
        foreach (var packaged in template.PackagedAssets)
        {
            var assetPath = VisualPageProjectService.ResolveProjectPath(
                directory,
                packaged.Value);
            var asset = new FileInfo(assetPath);
            if (!asset.Exists
                || asset.Length is <= 0 or > MaximumPackagedAssetBytes)
            {
                throw new VisualPageValidationException(
                    "A packaged template asset is missing or exceeds its bounded limit.");
            }
            VisualPageProjectService.RejectReparsePoint(asset.FullName);
            totalBytes += asset.Length;
            if (totalBytes > MaximumPackagedAssetTotalBytes)
            {
                throw new VisualPageValidationException(
                    "Packaged template assets exceed the total bounded limit.");
            }
        }
    }

    private static async Task CopyAtomicAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var temporary = destinationPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var input = new FileStream(
                             sourcePath,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            File.Move(temporary, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string SafeIdPrefix(string templateId)
    {
        var safe = new string(templateId.Where(char.IsLetterOrDigit).ToArray());
        if (safe.Length < 8)
        {
            throw new VisualPageValidationException(
                "Personal templates require a stable alphanumeric ID.");
        }
        return safe[..8].ToLowerInvariant();
    }

    private static bool IsSafeAssetId(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length is >= 8 and <= 128
        && value.All(character => char.IsLetterOrDigit(character)
                                  || character is '-' or '_' or '.');

    private static IReadOnlyList<VisualPageTemplateDocument> CreateBuiltIns()
    {
        var atlasArchiveTemplates = VisualPageAtlasTemplateCatalog.CreateTemplates();
        var commonTemplates = VisualPageCommonTemplateCatalog.CreateTemplates();
        var hero = new VisualPageTemplateDocument
        {
            TemplateId = "atlas.hero.split.v1",
            Name = "Atlas split hero",
            Description = "Responsive copy and media hero with a primary action.",
            Scope = "section",
            Library = VisualPageAtlasTemplateCatalog.LibraryName,
            Category = "Editorial sections",
            UsageHint = "Use for an image-led landing page or a short Archive introduction.",
            Tags = ["atlas", "hero", "media", "section"],
            Root = new VisualPageElement
            {
                DescriptorId = "atlas.hero",
                Name = "Atlas split hero",
                Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["aria-label"] = "Page introduction",
                    ["class"] = "atlas-hero"
                },
                Layout = new Dictionary<VisualPageBreakpoint, VisualPageLayoutSpec>
                {
                    [VisualPageBreakpoint.Base] = new()
                    {
                        Display = "flex",
                        FlexDirection = "column",
                        Gap = "2rem",
                        Padding = "clamp(3rem, 8vw, 7rem) clamp(1rem, 4vw, 5rem)"
                    },
                    [VisualPageBreakpoint.Large] = new()
                    {
                        Display = "grid",
                        GridTemplateColumns = "minmax(0, 7fr) minmax(16rem, 5fr)",
                        Gap = "clamp(2rem, 5vw, 5rem)"
                    }
                },
                Children =
                [
                    new VisualPageElement
                    {
                        DescriptorId = "core.container",
                        Name = "Hero copy",
                        Children =
                        [
                            new VisualPageElement
                            {
                                DescriptorId = "core.heading1",
                                Name = "Heading",
                                Text = "A clear, compelling page title"
                            },
                            new VisualPageElement
                            {
                                DescriptorId = "core.paragraph",
                                Name = "Summary",
                                Text = "Explain what this page offers and why it matters."
                            },
                            new VisualPageElement
                            {
                                DescriptorId = "core.button",
                                Name = "Primary action",
                                Text = "Explore"
                            }
                        ]
                    },
                    new VisualPageElement
                    {
                        DescriptorId = "core.image",
                        Name = "Hero image",
                        Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["src"] = "asset:hero-image",
                            ["alt"] = "Describe the hero image",
                            ["loading"] = "eager"
                        }
                    }
                ]
            },
            AssetIds = ["hero-image"]
        };
        var cards = new VisualPageTemplateDocument
        {
            TemplateId = "atlas.card-grid.v1",
            Name = "Atlas card grid",
            Description = "Three responsive editorial cards.",
            Scope = "section",
            Library = VisualPageAtlasTemplateCatalog.LibraryName,
            Category = "Cards & links",
            UsageHint = "Use for related topics, chapter choices, or an Archive index.",
            Tags = ["atlas", "cards", "links", "section"],
            Root = new VisualPageElement
            {
                DescriptorId = "atlas.card-grid",
                Name = "Card grid",
                Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["aria-label"] = "Featured content",
                    ["class"] = "atlas-card-grid"
                },
                Layout = new Dictionary<VisualPageBreakpoint, VisualPageLayoutSpec>
                {
                    [VisualPageBreakpoint.Base] = new()
                    {
                        Display = "grid",
                        GridTemplateColumns = "repeat(auto-fit, minmax(16rem, 1fr))",
                        Gap = "1.25rem"
                    }
                },
                Children = Enumerable.Range(1, 3)
                    .Select(index => new VisualPageElement
                    {
                        DescriptorId = "atlas.card",
                        Name = $"Card {index}",
                        Children =
                        [
                            new VisualPageElement
                            {
                                DescriptorId = "core.heading3",
                                Name = $"Card {index} heading",
                                Text = $"Featured idea {index}"
                            },
                            new VisualPageElement
                            {
                                DescriptorId = "core.paragraph",
                                Name = $"Card {index} summary",
                                Text = "Add a concise description that helps readers choose what to explore."
                            },
                            new VisualPageElement
                            {
                                DescriptorId = "core.link",
                                Name = $"Card {index} link",
                                Text = "Read more",
                                Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
                                {
                                    ["href"] = "#"
                                }
                            }
                        ]
                    }).ToList()
            }
        };
        var cta = new VisualPageTemplateDocument
        {
            TemplateId = "atlas.cta.v1",
            Name = "Atlas call to action",
            Description = "Responsive callout with supporting copy and action.",
            Scope = "section",
            Library = VisualPageAtlasTemplateCatalog.LibraryName,
            Category = "Cards & links",
            UsageHint = "Use when a page has one clear next action.",
            Tags = ["atlas", "call to action", "button", "section"],
            Root = new VisualPageElement
            {
                DescriptorId = "atlas.cta",
                Name = "Call to action",
                Children =
                [
                    new VisualPageElement
                    {
                        DescriptorId = "core.container",
                        Name = "CTA copy",
                        Children =
                        [
                            new VisualPageElement
                            {
                                DescriptorId = "core.heading2",
                                Name = "CTA heading",
                                Text = "Ready for the next step?"
                            },
                            new VisualPageElement
                            {
                                DescriptorId = "core.paragraph",
                                Name = "CTA summary",
                                Text = "Give readers one clear reason to continue."
                            }
                        ]
                    },
                    new VisualPageElement
                    {
                        DescriptorId = "core.button",
                        Name = "CTA button",
                        Text = "Continue"
                    }
                ]
            }
        };
        var navigation = new VisualPageTemplateDocument
        {
            TemplateId = "core.navigation.header.v1",
            Name = "Responsive navigation header",
            Description = "Accessible brand link and primary navigation that wraps on narrow screens.",
            Scope = "section",
            Library = "Core essentials",
            Category = "Navigation",
            UsageHint = "Use in a standalone local project; Atlas-hosted Archive pages inherit the site shell.",
            Tags = ["navigation", "header", "responsive", "section"],
            Root = new VisualPageElement
            {
                DescriptorId = "html.header",
                Name = "Site header",
                Layout = new Dictionary<VisualPageBreakpoint, VisualPageLayoutSpec>
                {
                    [VisualPageBreakpoint.Base] = new()
                    {
                        Display = "flex",
                        FlexDirection = "row",
                        FlexWrap = "wrap",
                        AlignItems = "center",
                        JustifyContent = "space-between",
                        Gap = "1rem",
                        Padding = "1rem clamp(1rem, 4vw, 4rem)"
                    }
                },
                Children =
                [
                    new VisualPageElement
                    {
                        DescriptorId = "core.link",
                        Name = "Brand link",
                        Text = "Site name",
                        Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["href"] = "/",
                            ["aria-label"] = "Home"
                        }
                    },
                    new VisualPageElement
                    {
                        DescriptorId = "core.nav",
                        Name = "Primary navigation",
                        Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["aria-label"] = "Primary navigation"
                        },
                        Layout = new Dictionary<VisualPageBreakpoint, VisualPageLayoutSpec>
                        {
                            [VisualPageBreakpoint.Base] = new()
                            {
                                Display = "flex",
                                FlexDirection = "row",
                                FlexWrap = "wrap",
                                Gap = "1rem"
                            }
                        },
                        Children = Enumerable.Range(1, 3).Select(index =>
                            new VisualPageElement
                            {
                                DescriptorId = "core.link",
                                Name = $"Navigation link {index}",
                                Text = $"Link {index}",
                                Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
                                {
                                    ["href"] = "#"
                                }
                            }).ToList()
                    }
                ]
            }
        };
        var contactForm = new VisualPageTemplateDocument
        {
            TemplateId = "core.contact.form.v1",
            Name = "Accessible contact form",
            Description = "Native name, email, message, and submit controls ready for a handler.",
            Scope = "section",
            Library = "Core essentials",
            Category = "Forms",
            UsageHint = "Add a reviewed submit handler before publishing.",
            Tags = ["form", "contact", "accessible", "section"],
            Root = new VisualPageElement
            {
                DescriptorId = "html.form",
                Name = "Contact form",
                Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["aria-label"] = "Contact form",
                    ["method"] = "post"
                },
                Layout = new Dictionary<VisualPageBreakpoint, VisualPageLayoutSpec>
                {
                    [VisualPageBreakpoint.Base] = new()
                    {
                        Display = "flex",
                        FlexDirection = "column",
                        Gap = "0.75rem",
                        MaxWidth = "42rem"
                    }
                },
                Children =
                [
                    new VisualPageElement { DescriptorId = "core.heading2", Name = "Form heading", Text = "Contact us" },
                    new VisualPageElement { DescriptorId = "core.label", Name = "Name label", Text = "Name", Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["for"] = "contact-name" } },
                    new VisualPageElement { DescriptorId = "html.input.text", Name = "Name input", Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["type"] = "text", ["id"] = "contact-name", ["name"] = "name", ["autocomplete"] = "name", ["required"] = "true", ["aria-label"] = "Name" } },
                    new VisualPageElement { DescriptorId = "core.label", Name = "Email label", Text = "Email", Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["for"] = "contact-email" } },
                    new VisualPageElement { DescriptorId = "html.input.email", Name = "Email input", Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["type"] = "email", ["id"] = "contact-email", ["name"] = "email", ["autocomplete"] = "email", ["required"] = "true", ["aria-label"] = "Email" } },
                    new VisualPageElement { DescriptorId = "core.label", Name = "Message label", Text = "Message", Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["for"] = "contact-message" } },
                    new VisualPageElement { DescriptorId = "html.textarea", Name = "Message input", Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["id"] = "contact-message", ["name"] = "message", ["rows"] = "6", ["required"] = "true", ["aria-label"] = "Message" } },
                    new VisualPageElement { DescriptorId = "core.button", Name = "Submit button", Text = "Send message", Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["type"] = "submit" } }
                ]
            }
        };
        var faq = new VisualPageTemplateDocument
        {
            TemplateId = "core.faq.disclosure.v1",
            Name = "FAQ disclosures",
            Description = "Accessible heading and native disclosure controls for common questions.",
            Scope = "section",
            Library = "Core essentials",
            Category = "Editorial sections",
            UsageHint = "Use for questions, definitions, transcripts, or optional technical detail.",
            Tags = ["faq", "details", "disclosure", "section"],
            Root = new VisualPageElement
            {
                DescriptorId = "core.section",
                Name = "Frequently asked questions",
                Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["aria-label"] = "Frequently asked questions"
                },
                Children =
                [
                    new VisualPageElement { DescriptorId = "core.heading2", Name = "FAQ heading", Text = "Frequently asked questions" },
                    .. Enumerable.Range(1, 3).Select(index => new VisualPageElement
                    {
                        DescriptorId = "html.details",
                        Name = $"Question {index}",
                        Children =
                        [
                            new VisualPageElement { DescriptorId = "html.summary", Name = $"Question {index} summary", Text = $"Question {index}?" },
                            new VisualPageElement { DescriptorId = "core.paragraph", Name = $"Answer {index}", Text = "Add a concise, useful answer here." }
                        ]
                    })
                ]
            }
        };
        var table = new VisualPageTemplateDocument
        {
            TemplateId = "core.accessible.table.v1",
            Name = "Accessible data table",
            Description = "Captioned table with scoped column headings and sample rows.",
            Scope = "section",
            Library = "Core essentials",
            Category = "Media & data",
            UsageHint = "Replace the sample values and keep the caption specific.",
            Tags = ["table", "data", "accessible", "section"],
            Root = new VisualPageElement
            {
                DescriptorId = "html.table",
                Name = "Data table",
                Children =
                [
                    new VisualPageElement { DescriptorId = "html.caption", Name = "Table caption", Text = "Data table caption" },
                    new VisualPageElement
                    {
                        DescriptorId = "html.thead",
                        Name = "Table headings",
                        Children =
                        [
                            new VisualPageElement
                            {
                                DescriptorId = "html.tr",
                                Name = "Heading row",
                                Children = Enumerable.Range(1, 3).Select(index => new VisualPageElement
                                {
                                    DescriptorId = "html.th",
                                    Name = $"Column {index}",
                                    Text = $"Column {index}",
                                    Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["scope"] = "col" }
                                }).ToList()
                            }
                        ]
                    },
                    new VisualPageElement
                    {
                        DescriptorId = "html.tbody",
                        Name = "Table body",
                        Children = Enumerable.Range(1, 2).Select(row => new VisualPageElement
                        {
                            DescriptorId = "html.tr",
                            Name = $"Row {row}",
                            Children = Enumerable.Range(1, 3).Select(column => new VisualPageElement
                            {
                                DescriptorId = "html.td",
                                Name = $"Row {row} cell {column}",
                                Text = $"Value {row}.{column}"
                            }).ToList()
                        }).ToList()
                    }
                ]
            }
        };
        return [
            .. atlasArchiveTemplates,
            .. commonTemplates,
            .. StudioApplicationPageTemplates.CreateTemplates(),
            .. StudioApplicationSectionTemplates.CreateTemplates(),
            .. StudioApplicationModuleTemplates.CreateTemplates(),
            hero,
            cards,
            cta,
            navigation,
            contactForm,
            faq,
            table
        ];
    }

    private static IEnumerable<VisualPageElement> Flatten(VisualPageElement root)
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
}

public sealed class VisualPageImportResult
{
    public required VisualPageElement Root { get; init; }
    public required IReadOnlyList<VisualPageDiagnostic> Diagnostics { get; init; }
    public bool ContainsCustomCode { get; init; }
}

public sealed record VisualPageCanvasOptions(
    double ZoomPercent = 100,
    string ViewportWidth = "Auto",
    bool ShowGrid = false,
    bool ShowOutlines = false);

public sealed partial class VisualPageImportService
{
    public const int MaximumImportCharacters = 512 * 1024;
    private readonly VisualPageComponentCatalog _catalog;

    public VisualPageImportService(VisualPageComponentCatalog catalog)
    {
        _catalog = catalog;
    }

    public VisualPageImportResult Import(string source, bool razor)
    {
        if (source.Length > MaximumImportCharacters)
        {
            throw new VisualPageValidationException(
                "Imports are limited to 512 KB of markup.");
        }

        var root = _catalog.CreateElement("core.container");
        root.Name = "Imported content";
        var stack = new Stack<(string Tag, VisualPageElement Element)>();
        stack.Push(("root", root));
        var diagnostics = new List<VisualPageDiagnostic>();
        var cursor = 0;
        var custom = false;
        foreach (Match match in MarkupTokenRegex().Matches(source))
        {
            if (match.Index > cursor)
            {
                AddTextOrCustom(
                    stack.Peek().Element,
                    source[cursor..match.Index],
                    razor,
                    diagnostics,
                    ref custom);
            }

            var token = match.Value;
            if (token.StartsWith("<!--", StringComparison.Ordinal))
            {
                cursor = match.Index + match.Length;
                continue;
            }

            var tagMatch = TagRegex().Match(token);
            if (!tagMatch.Success)
            {
                AddCustom(stack.Peek().Element, token, diagnostics, ref custom);
                cursor = match.Index + match.Length;
                continue;
            }

            var closing = tagMatch.Groups["close"].Success;
            var tag = tagMatch.Groups["tag"].Value;
            var importedAttributes = ParseAttributes(
                tagMatch.Groups["attrs"].Value);
            var selfClosing = token.EndsWith("/>", StringComparison.Ordinal);
            if (closing)
            {
                if (stack.Count > 1
                    && stack.Peek().Tag.Equals(tag, StringComparison.OrdinalIgnoreCase))
                {
                    stack.Pop();
                }
                else
                {
                    AddCustom(stack.Peek().Element, token, diagnostics, ref custom);
                }
            }
            else if (_catalog.TryGetHtmlElement(
                         tag,
                         importedAttributes,
                         out var descriptor))
            {
                var element = _catalog.CreateElement(descriptor.Id);
                element.Name = $"Imported {tag}";
                // Catalog defaults make newly inserted elements immediately
                // useful, but imported markup must preserve its own text rather
                // than retaining designer placeholder copy.
                element.Text = string.Empty;
                ImportAttributes(element, tagMatch.Groups["attrs"].Value, diagnostics);
                stack.Peek().Element.Children.Add(element);
                // Track every non-void element until its closing tag, including
                // elements with an empty element-child model such as iframe and
                // selectedcontent. Otherwise their legitimate closing tag would
                // be misclassified as preserved custom Razor.
                if (!selfClosing && !descriptor.IsVoid)
                {
                    stack.Push((tag, element));
                }
            }
            else
            {
                AddCustom(stack.Peek().Element, token, diagnostics, ref custom);
            }
            cursor = match.Index + match.Length;
        }

        if (cursor < source.Length)
        {
            AddTextOrCustom(
                stack.Peek().Element,
                source[cursor..],
                razor,
                diagnostics,
                ref custom);
        }
        if (stack.Count > 1)
        {
            diagnostics.Add(new VisualPageDiagnostic
            {
                Code = "VPI003",
                Severity = VisualPageDiagnosticSeverity.Warning,
                Message = "The imported markup contains unclosed elements; supported content was preserved."
            });
        }
        return new VisualPageImportResult
        {
            Root = root,
            Diagnostics = diagnostics,
            ContainsCustomCode = custom
        };
    }

    private static void AddTextOrCustom(
        VisualPageElement parent,
        string text,
        bool razor,
        List<VisualPageDiagnostic> diagnostics,
        ref bool custom)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        if (razor && (text.Contains('@') || text.Contains("@{", StringComparison.Ordinal)))
        {
            AddCustom(parent, text, diagnostics, ref custom);
            return;
        }
        var normalized = WebUtility.HtmlDecode(text).Trim();
        if (parent.Children.Count == 0 && string.IsNullOrWhiteSpace(parent.Text))
        {
            parent.Text = normalized;
        }
        else
        {
            parent.Children.Add(new VisualPageElement
            {
                DescriptorId = "core.paragraph",
                Name = "Imported text",
                Text = normalized
            });
        }
    }

    private static void AddCustom(
        VisualPageElement parent,
        string source,
        List<VisualPageDiagnostic> diagnostics,
        ref bool custom)
    {
        parent.Children.Add(new VisualPageElement
        {
            DescriptorId = "custom.razor",
            Name = "Preserved custom code",
            CustomRazor = source
        });
        diagnostics.Add(new VisualPageDiagnostic
        {
            Code = "VPI001",
            Severity = VisualPageDiagnosticSeverity.Warning,
            Message = "Unsupported markup was preserved as an opaque custom-code node."
        });
        custom = true;
    }

    private static void ImportAttributes(
        VisualPageElement element,
        string source,
        List<VisualPageDiagnostic> diagnostics)
    {
        foreach (Match match in AttributeRegex().Matches(source))
        {
            var name = match.Groups["name"].Value;
            var value = match.Groups["value"].Value;
            if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase)
                || name.Equals("style", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new VisualPageDiagnostic
                {
                    Code = "VPI002",
                    Severity = VisualPageDiagnosticSeverity.Warning,
                    Message = $"Inline attribute '{name}' was not imported into the design canvas."
                });
                continue;
            }
            element.Attributes[name] = WebUtility.HtmlDecode(value);
        }
    }

    private static IReadOnlyDictionary<string, string> ParseAttributes(
        string source)
    {
        var attributes = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AttributeRegex().Matches(source))
        {
            attributes[match.Groups["name"].Value] =
                WebUtility.HtmlDecode(match.Groups["value"].Value);
        }
        return attributes;
    }

    [GeneratedRegex("<!--[\\s\\S]*?-->|<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex MarkupTokenRegex();

    [GeneratedRegex("^<\\s*(?<close>/)?\\s*(?<tag>[A-Za-z][A-Za-z0-9:-]*)(?<attrs>[\\s\\S]*?)/?\\s*>$", RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    [GeneratedRegex("(?<name>[A-Za-z_:][A-Za-z0-9_.:-]*)(?:\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s>]+)))?", RegexOptions.CultureInvariant)]
    private static partial Regex AttributeRegex();
}

public sealed class VisualPageDesignCanvasRenderer
{
    private readonly VisualPageComponentCatalog _catalog;

    public VisualPageDesignCanvasRenderer(VisualPageComponentCatalog catalog)
    {
        _catalog = catalog;
    }

    public string Render(
        VisualPageDocument page,
        string? selectedElementId,
        VisualPageBreakpoint breakpoint,
        IReadOnlyDictionary<string, string>? canvasAssetSources = null,
        VisualPageCanvasOptions? options = null,
        IReadOnlyList<string>? stylesheetUrls = null,
        IReadOnlyList<string>? foundationStylesheetUrls = null)
    {
        options ??= new VisualPageCanvasOptions();
        var zoom = Math.Clamp(options.ZoomPercent, 25, 400) / 100d;
        var viewportWidth = int.TryParse(
            options.ViewportWidth,
            out var parsedViewportWidth)
            ? $"{Math.Clamp(parsedViewportWidth, 280, 2560)}px"
            : "100%";
        var bodyClasses = string.Join(
            ' ',
            new[]
            {
                options.ShowGrid ? "vp-grid" : string.Empty,
                options.ShowOutlines ? "vp-outlines" : string.Empty
            }.Where(value => value.Length > 0));
        var body = new StringBuilder();
        RenderElement(
            body,
            page.Root,
            selectedElementId,
            depth: 0,
            canvasAssetSources);
        var css = new StringBuilder();
        foreach (var element in Flatten(page.Root))
        {
            RenderElementCss(css, element, breakpoint);
        }
        var stylesheetLinks = string.Join("\n", (stylesheetUrls ?? [])
            .Select(url => $"<link rel=\"stylesheet\" href=\"{WebUtility.HtmlEncode(url)}\">"));
        var foundationStylesheetLinks = string.Join("\n", (foundationStylesheetUrls ?? [])
            .Select(url => $"<link rel=\"stylesheet\" href=\"{WebUtility.HtmlEncode(url)}\">"));

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              {{foundationStylesheetLinks}}
              <style>
                :root { color-scheme: dark; --canvas-accent:#58a6ff; --canvas-bg:#0b0f14; --canvas-text:#f0f6fc; }
                * { box-sizing: border-box; }
                html, body { margin:0; min-height:100%; background:var(--canvas-bg); color:var(--canvas-text); font-family:Inter,Segoe UI,sans-serif; }
                body { padding:24px; overflow:auto; }
                body.vp-grid { background-image:linear-gradient(rgba(88,166,255,.08) 1px,transparent 1px),linear-gradient(90deg,rgba(88,166,255,.08) 1px,transparent 1px); background-size:16px 16px; }
                #vp-canvas-root { width:{{viewportWidth}}; min-height:calc(100vh - 48px); margin:0 auto; zoom:{{zoom.ToString("0.##", CultureInfo.InvariantCulture)}}; background:var(--canvas-bg); box-shadow:0 0 0 1px rgba(240,246,252,.08),0 18px 60px rgba(0,0,0,.35); }
                body.vp-grid #vp-canvas-root { background-image:linear-gradient(rgba(88,166,255,.08) 1px,transparent 1px),linear-gradient(90deg,rgba(88,166,255,.08) 1px,transparent 1px); background-size:16px 16px; }
                [data-vp-id] { position:relative; min-width:8px; min-height:8px; transition:outline-color .12s,background .12s; }
                [data-vp-id]:hover { outline:1px dashed rgba(88,166,255,.75); outline-offset:3px; }
                body.vp-outlines [data-vp-id] { outline:1px dashed rgba(139,148,158,.45); outline-offset:-1px; }
                [data-vp-selected="true"] { outline:2px solid var(--canvas-accent)!important; outline-offset:4px; }
                [data-vp-selected="true"]::after { content:attr(data-vp-label); position:absolute; left:0; top:-24px; z-index:10; padding:3px 6px; border-radius:4px 4px 0 0; background:var(--canvas-accent); color:#08111c; font:600 10px ui-monospace,monospace; white-space:nowrap; }
                [data-vp-locked="true"]::before { content:'locked'; position:absolute; top:3px; right:3px; font-size:9px; color:#d29922; }
                .vp-drop-zone { display:block; height:8px; margin:2px 0; border-radius:4px; }
                .vp-drop-zone.vp-over { background:var(--canvas-accent); }
                .vp-custom { border:1px dashed #d29922; background:#2d2208; color:#f2cc60; padding:16px; border-radius:8px; font:12px ui-monospace,monospace; }
                .vp-placeholder { border:1px dashed #8b949e; background:linear-gradient(145deg,#161b22,#11161d); color:#c9d1d9; padding:12px; border-radius:7px; font:11px ui-monospace,monospace; }
                .vp-placeholder-header { display:flex; align-items:center; gap:8px; margin-bottom:6px; }
                .vp-placeholder-title { color:#79c0ff; font-weight:700; }
                .vp-placeholder-badge { margin-left:auto; padding:2px 6px; border:1px solid #3d4c5c; border-radius:999px; color:#a5d6ff; background:#0d2035; font-size:8px; letter-spacing:.05em; }
                .vp-placeholder-name { color:#f0f6fc; font:600 12px Inter,Segoe UI,sans-serif; margin-bottom:3px; }
                .vp-placeholder-description { color:#9da7b3; font:10px/1.45 Inter,Segoe UI,sans-serif; margin-bottom:5px; }
                .vp-placeholder-guidance { color:#8b949e; font:9px/1.4 Inter,Segoe UI,sans-serif; }
                .vp-placeholder-attributes { display:flex; flex-wrap:wrap; gap:4px; margin-top:7px; }
                .vp-placeholder-attribute { padding:2px 5px; border-radius:4px; background:#21262d; color:#c9d1d9; font-size:9px; }
                .vp-binding { display:inline-block; padding:2px 5px; border-radius:4px; background:#1f6feb55; color:#a5d6ff; font:10px ui-monospace,monospace; }
                .vp-resize { position:absolute; right:-7px; bottom:-7px; width:14px; height:14px; border-radius:50%; background:var(--canvas-accent); cursor:nwse-resize; z-index:9; }
                img { max-width:100%; height:auto; }
                button,input,select { font:inherit; }
                {{css}}
              </style>
              {{stylesheetLinks}}
            </head>
            <body class="{{bodyClasses}}" data-breakpoint="{{breakpoint.ToString().ToLowerInvariant()}}">
              <div id="vp-canvas-root">{{body}}</div>
              <script>
                (() => {
                  const send = value => window.chrome?.webview?.postMessage(JSON.stringify(value));
                  let dragged = null;
                  let resize = null;
                  let spaceDown = false;
                  let pan = null;
                  document.addEventListener('click', event => {
                    const node = event.target.closest('[data-vp-id]');
                    if (!node) return;
                    event.preventDefault(); event.stopPropagation();
                    send({ kind:'select', elementId:node.dataset.vpId });
                  });
                  document.addEventListener('contextmenu', event => {
                    const node = event.target.closest('[data-vp-id]');
                    if (!node) return;
                    event.preventDefault();
                    send({ kind:'context', elementId:node.dataset.vpId, x:event.clientX, y:event.clientY });
                  });
                  document.addEventListener('dragstart', event => {
                    const node = event.target.closest('[data-vp-id]');
                    if (!node || node.dataset.vpLocked === 'true') { event.preventDefault(); return; }
                    dragged = node.dataset.vpId;
                    event.dataTransfer.effectAllowed='move';
                  });
                  document.querySelectorAll('.vp-drop-zone').forEach(zone => {
                    zone.addEventListener('dragover', event => { event.preventDefault(); zone.classList.add('vp-over'); });
                    zone.addEventListener('dragleave', () => zone.classList.remove('vp-over'));
                    zone.addEventListener('drop', event => {
                      event.preventDefault(); zone.classList.remove('vp-over');
                      if (dragged) send({kind:'move', elementId:dragged, parentElementId:zone.dataset.parentId, index:Number(zone.dataset.index)});
                      dragged=null;
                    });
                  });
                  document.querySelectorAll('.vp-resize').forEach(handle => handle.addEventListener('pointerdown', event => {
                    event.preventDefault(); event.stopPropagation();
                    const node=handle.closest('[data-vp-id]');
                    resize={id:node.dataset.vpId,x:event.clientX,y:event.clientY,w:node.getBoundingClientRect().width};
                    handle.setPointerCapture(event.pointerId);
                  }));
                  document.addEventListener('pointerup', event => {
                    if (!resize) return;
                    const delta=event.clientX-resize.x;
                    send({kind:'resize',elementId:resize.id,deltaX:delta,startWidth:resize.w});
                    resize=null;
                  });
                  document.addEventListener('keydown', event => {
                    if (event.code === 'Space' && !event.repeat) { spaceDown=true; document.body.style.cursor='grab'; event.preventDefault(); }
                    const control = event.ctrlKey || event.metaKey;
                    const command = control && event.key.toLowerCase()==='z' ? (event.shiftKey ? 'redo' : 'undo')
                      : control && event.key.toLowerCase()==='y' ? 'redo'
                      : control && event.key.toLowerCase()==='d' ? 'duplicate'
                      : event.key==='Delete' ? 'delete'
                      : event.altKey && event.key==='ArrowUp' ? 'move-up'
                      : event.altKey && event.key==='ArrowDown' ? 'move-down'
                      : event.key==='Escape' ? 'select-parent' : null;
                    if (command) { event.preventDefault(); send({kind:'command',command}); }
                  });
                  document.addEventListener('keyup', event => {
                    if (event.code === 'Space') { spaceDown=false; pan=null; document.body.style.cursor=''; }
                  });
                  document.addEventListener('pointerdown', event => {
                    if (!spaceDown) return;
                    event.preventDefault();
                    pan={x:event.clientX,y:event.clientY,left:window.scrollX,top:window.scrollY};
                    document.body.style.cursor='grabbing';
                  }, true);
                  document.addEventListener('pointermove', event => {
                    if (!pan) return;
                    window.scrollTo(pan.left-(event.clientX-pan.x),pan.top-(event.clientY-pan.y));
                  }, true);
                  document.addEventListener('pointerup', () => {
                    if (pan) { pan=null; document.body.style.cursor=spaceDown?'grab':''; }
                  }, true);
                  document.addEventListener('wheel', event => {
                    if (!event.ctrlKey) return;
                    event.preventDefault();
                    send({kind:'zoom',delta:event.deltaY < 0 ? 10 : -10});
                  }, {passive:false});
                })();
              </script>
            </body>
            </html>
            """;
    }

    private void RenderElement(
        StringBuilder output,
        VisualPageElement element,
        string? selectedElementId,
        int depth,
        IReadOnlyDictionary<string, string>? canvasAssetSources)
    {
        var descriptor = _catalog.GetRequired(element.DescriptorId);
        var indent = new string(' ', depth * 2);
        if (element.IsHidden)
        {
            return;
        }
        if (element.DescriptorId == "custom.razor")
        {
            output.Append(indent).Append("<div class=\"vp-custom\" data-vp-id=\"")
                .Append(WebUtility.HtmlEncode(element.ElementId))
                .Append("\" draggable=\"true\">Custom Razor fragment · previewed only after reviewed build<div class=\"vp-resize\"></div></div>\n");
            return;
        }

        if (descriptor.CanvasRepresentation ==
            VisualPageCanvasRepresentation.Placeholder)
        {
            RenderPlaceholder(
                output,
                element,
                descriptor,
                selectedElementId,
                depth,
                canvasAssetSources);
            return;
        }

        var tag = CanvasTag(descriptor.MarkupName);
        var isVoid = descriptor.IsVoid;
        output.Append(indent).Append('<').Append(tag)
            .Append(" data-vp-id=\"").Append(WebUtility.HtmlEncode(element.ElementId)).Append('"')
            .Append(" data-vp-label=\"").Append(WebUtility.HtmlEncode($"{element.Name} · <{descriptor.MarkupName}>")).Append('"')
            .Append(" data-vp-selected=\"").Append(string.Equals(element.ElementId, selectedElementId, StringComparison.Ordinal) ? "true" : "false").Append('"')
            .Append(" data-vp-locked=\"").Append(element.IsLocked ? "true" : "false").Append('"')
            .Append(" draggable=\"").Append(element.IsLocked ? "false" : "true").Append('"');
        foreach (var attribute in element.Attributes.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            var value = attribute.Value;
            if (canvasAssetSources is not null
                && (attribute.Key.Equals("src", StringComparison.OrdinalIgnoreCase)
                    || attribute.Key.Equals("poster", StringComparison.OrdinalIgnoreCase))
                && canvasAssetSources.TryGetValue(
                    value.Replace('\\', '/'),
                    out var dataSource))
            {
                value = dataSource;
            }
            if (!IsSafeCanvasAttribute(attribute.Key, value))
            {
                continue;
            }
            if (_catalog.TryGetAttributeDefinition(
                    descriptor,
                    attribute.Key,
                    out var definition)
                && definition.EditorKind ==
                VisualPageAttributeEditorKind.Boolean)
            {
                if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                output.Append(' ').Append(WebUtility.HtmlEncode(attribute.Key));
                continue;
            }
            output.Append(' ').Append(WebUtility.HtmlEncode(attribute.Key))
                .Append("=\"").Append(WebUtility.HtmlEncode(value)).Append('"');
        }
        if (isVoid)
        {
            output.Append(">\n");
            return;
        }
        output.Append('>');
        if (!string.IsNullOrWhiteSpace(element.Text))
        {
            output.Append(WebUtility.HtmlEncode(element.Text));
        }
        foreach (var binding in element.Bindings)
        {
            output.Append("<span class=\"vp-binding\">")
                .Append(WebUtility.HtmlEncode($"{binding.Kind}: {binding.Expression}"))
                .Append("</span>");
        }
        output.Append('\n');
        if (descriptor.AllowsChildren)
        {
            output.Append(indent).Append("  <span class=\"vp-drop-zone\" data-parent-id=\"")
                .Append(WebUtility.HtmlEncode(element.ElementId)).Append("\" data-index=\"0\"></span>\n");
            for (var index = 0; index < element.Children.Count; index++)
            {
                RenderElement(
                    output,
                    element.Children[index],
                    selectedElementId,
                    depth + 1,
                    canvasAssetSources);
                output.Append(indent).Append("  <span class=\"vp-drop-zone\" data-parent-id=\"")
                    .Append(WebUtility.HtmlEncode(element.ElementId)).Append("\" data-index=\"")
                    .Append(index + 1).Append("\"></span>\n");
            }
        }
        output.Append(indent).Append("  <div class=\"vp-resize\"></div>\n")
            .Append(indent).Append("</").Append(tag).Append(">\n");
    }

    private void RenderPlaceholder(
        StringBuilder output,
        VisualPageElement element,
        VisualPageComponentDescriptor descriptor,
        string? selectedElementId,
        int depth,
        IReadOnlyDictionary<string, string>? canvasAssetSources)
    {
        var indent = new string(' ', depth * 2);
        output.Append(indent).Append("<div class=\"vp-placeholder\" data-vp-id=\"")
            .Append(WebUtility.HtmlEncode(element.ElementId))
            .Append("\" data-vp-label=\"")
            .Append(WebUtility.HtmlEncode($"{element.Name} · <{descriptor.MarkupName}>")).Append('"')
            .Append(" data-vp-selected=\"")
            .Append(string.Equals(element.ElementId, selectedElementId, StringComparison.Ordinal) ? "true" : "false")
            .Append("\" data-vp-locked=\"").Append(element.IsLocked ? "true" : "false")
            .Append("\" draggable=\"").Append(element.IsLocked ? "false" : "true").Append("\">\n")
            .Append(indent).Append("  <div class=\"vp-placeholder-header\">\n")
            .Append(indent).Append("    <span class=\"vp-placeholder-title\">&lt;")
            .Append(WebUtility.HtmlEncode(descriptor.MarkupName)).Append("&gt;</span>\n")
            .Append(indent).Append("    <span class=\"vp-placeholder-badge\">")
            .Append(WebUtility.HtmlEncode(descriptor.Authoring.Badge)).Append("</span>\n")
            .Append(indent).Append("  </div>\n")
            .Append(indent).Append("  <div class=\"vp-placeholder-name\">")
            .Append(WebUtility.HtmlEncode(descriptor.DisplayName)).Append("</div>\n")
            .Append(indent).Append("  <div class=\"vp-placeholder-description\">")
            .Append(WebUtility.HtmlEncode(descriptor.Description)).Append("</div>\n")
            .Append(indent).Append("  <div class=\"vp-placeholder-guidance\">")
            .Append(WebUtility.HtmlEncode(descriptor.Authoring.PlacementGuidance)).Append("</div>\n");
        if (element.Attributes.Count > 0)
        {
            output.Append(indent).Append("  <div class=\"vp-placeholder-attributes\">\n");
            foreach (var attribute in element.Attributes
                         .OrderBy(value => value.Key, StringComparer.Ordinal)
                         .Take(6))
            {
                output.Append(indent).Append("    <span class=\"vp-placeholder-attribute\">")
                    .Append(WebUtility.HtmlEncode(attribute.Key))
                    .Append(string.IsNullOrEmpty(attribute.Value)
                        ? string.Empty
                        : "=" + WebUtility.HtmlEncode(attribute.Value))
                    .Append("</span>\n");
            }
            output.Append(indent).Append("  </div>\n");
        }
        if (!string.IsNullOrWhiteSpace(element.Text))
        {
            output.Append(indent).Append("  <div>")
                .Append(WebUtility.HtmlEncode(element.Text)).Append("</div>\n");
        }
        if (descriptor.AllowsChildren)
        {
            output.Append(indent).Append("  <div class=\"vp-drop-zone\" data-parent-id=\"")
                .Append(WebUtility.HtmlEncode(element.ElementId)).Append("\" data-index=\"0\"></div>\n");
            for (var index = 0; index < element.Children.Count; index++)
            {
                RenderElement(output, element.Children[index], selectedElementId, depth + 1, canvasAssetSources);
                output.Append(indent).Append("  <div class=\"vp-drop-zone\" data-parent-id=\"")
                    .Append(WebUtility.HtmlEncode(element.ElementId)).Append("\" data-index=\"")
                    .Append(index + 1).Append("\"></div>\n");
            }
        }
        output.Append(indent).Append("  <div class=\"vp-resize\"></div>\n")
            .Append(indent).Append("</div>\n");
    }

    private static void RenderElementCss(
        StringBuilder output,
        VisualPageElement element,
        VisualPageBreakpoint breakpoint)
    {
        var styles = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var style in element.Styles)
        {
            if (IsSafeCanvasCss(style.Key, style.Value))
            {
                styles[style.Key] = style.Value;
            }
        }
        foreach (var candidate in Enum.GetValues<VisualPageBreakpoint>()
                     .Where(value => value <= breakpoint)
                     .OrderBy(value => value))
        {
            if (!element.Layout.TryGetValue(candidate, out var layout))
            {
                continue;
            }
            Add(styles, "display", layout.Display);
            Add(styles, "flex-direction", layout.FlexDirection);
            Add(styles, "flex-wrap", layout.FlexWrap);
            Add(styles, "grid-template-columns", layout.GridTemplateColumns);
            Add(styles, "flex-basis", layout.FlexBasis);
            Add(styles, "gap", layout.Gap);
            Add(styles, "padding", layout.Padding);
            Add(styles, "margin", layout.Margin);
            Add(styles, "min-width", layout.MinWidth);
            Add(styles, "max-width", layout.MaxWidth);
            Add(styles, "min-height", layout.MinHeight);
            Add(styles, "max-height", layout.MaxHeight);
            Add(styles, "align-items", layout.AlignItems);
            Add(styles, "justify-content", layout.JustifyContent);
            Add(styles, "align-self", layout.AlignSelf);
            if (layout.ColumnSpan is { } columns)
                styles["grid-column"] = $"span {Math.Clamp(columns, 1, 12)}";
            if (layout.RowSpan is { } rows)
                styles["grid-row"] = $"span {Math.Clamp(rows, 1, 12)}";
        }
        output.Append("[data-vp-id=\"").Append(CssEscape(element.ElementId)).Append("\"]{");
        foreach (var style in styles)
        {
            output.Append(style.Key).Append(':').Append(style.Value).Append(';');
        }
        output.Append("}\n");
    }

    private static void Add(
        IDictionary<string, string> target,
        string name,
        string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && IsSafeCanvasCss(name, value))
        {
            target[name] = value;
        }
    }

    private static bool IsSafeCanvasAttribute(string name, string value) =>
        !name.StartsWith("on", StringComparison.OrdinalIgnoreCase)
        && !name.Equals("srcdoc", StringComparison.OrdinalIgnoreCase)
        && !name.Equals("style", StringComparison.OrdinalIgnoreCase)
        && (!name.Equals("href", StringComparison.OrdinalIgnoreCase)
            && !name.Equals("src", StringComparison.OrdinalIgnoreCase)
            || Uri.TryCreate(value, UriKind.Relative, out _)
            || Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme is "http" or "https"
                || uri.Scheme == "data"
                && Regex.IsMatch(
                    value,
                    "^data:image/(?:png|jpeg|gif|webp|avif);base64,",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)));

    private static bool IsSafeCanvasCss(string name, string value) =>
        Regex.IsMatch(name, "^[a-z-]+$", RegexOptions.CultureInvariant)
        && !value.Contains("expression", StringComparison.OrdinalIgnoreCase)
        && !value.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
        && !value.Contains("@import", StringComparison.OrdinalIgnoreCase)
        && !value.Contains('<')
        && !value.Contains('>');

    private static string CanvasTag(string markup) => markup switch
    {
        "EditForm" => "form",
        "InputText" => "input",
        "InputNumber" => "input",
        "InputCheckbox" => "input",
        "InputSelect" => "select",
        "ValidationMessage" => "span",
        "ValidationSummary" => "div",
        "DynamicComponent" => "div",
        _ when markup.StartsWith("vp:", StringComparison.Ordinal) => "div",
        _ => markup
    };

    private static IEnumerable<VisualPageElement> Flatten(VisualPageElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var descendant in Flatten(child))
                yield return descendant;
    }

    private static string CssEscape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
