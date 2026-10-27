using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HyperLib.Core;
using Hypershell.Models;

namespace Hypershell.Services;

public sealed record HyperlibLegacyTemplateModule(string GroupId, string GroupTitle, string SourceTemplateId,
    BlockDefinition Definition, string Razor, string Css);

/// <summary>Ports the existing Hyperpage ASTs through their actual generator into editable, portable Hyperlib components.</summary>
public static class HyperlibLegacyTemplateLibraryService
{
    private const string PlaceholderUrl = "/_content/HyperLib.Work/legacy/placeholder.svg";
    private static readonly SemaphoreSlim SeedGate = new(1, 1);

    public static IReadOnlyList<HyperlibLegacyTemplateModule> CreateModules()
    {
        var catalog = new VisualPageComponentCatalog();
        var validator = new VisualPageSchemaValidator(catalog);
        var generator = new VisualPageGenerator(catalog, validator);
        var templates = new VisualPageTemplateService(catalog).BuiltInTemplates;
        return templates.Select(template => Convert(template, generator, catalog)).ToList();
    }

    public static async Task<IReadOnlyList<TemplateDesignGroup>> EnsureLegacyDesignGroupsAsync(string libraryRoot, CancellationToken ct = default, string? onlyGroup = null)
    {
        await SeedGate.WaitAsync(ct);
        try
        {
            var result = new List<TemplateDesignGroup>();
            var groups = CreateModules().Where(module => onlyGroup is null
                ? !HyperlibWorkProjectService.IsApplicationTemplateGroup(module.GroupId)
                : onlyGroup == HyperlibWorkProjectService.ApplicationTemplateGroup ? HyperlibWorkProjectService.IsApplicationTemplateGroup(module.GroupId)
                : module.GroupId == onlyGroup).GroupBy(module => module.GroupId);
            foreach (var modules in groups)
            {
                ct.ThrowIfCancellationRequested();
                var existing = Path.Combine(libraryRoot, "CoreLibrary", "DesignGroups", modules.Key);
                if (Directory.Exists(existing)) { result.Add(TemplateObjectLibrary.GetDesignGroup(libraryRoot, modules.Key)); continue; }
                var first = modules.First();
                var projectRoot = VisualPageProjectService.ResolveProjectPath(libraryRoot, ".hyperlib/catalog-seeds/legacy-v1/" + modules.Key + "/Project");
                await TemplateProjectScaffolder.EnsureAsync(libraryRoot, projectRoot, modules.Key, first.GroupTitle, ct, ReusableTemplateCatalog.BlankId, exportingCatalog: true);
                var definition = ReusableTemplateCatalog.CreateBuiltInDefinition(ReusableTemplateCatalog.BlankId);
                definition.Title = first.GroupTitle;
                definition.Blocks = modules.Select(module => HyperlibTemplateEditSession.CloneElement(module.Definition)).ToList();
                definition.Regions = [new() { Id = "main", Title = "Page content" }];
                definition.DefaultBlocks = [];
                if (HyperlibWorkProjectService.IsApplicationTemplateGroup(modules.Key)) definition.Appearance.Clear();
                definition.EmptyPageDefault = true;
                TemplateValidator.ValidateDefinition(definition);
                foreach (var module in modules)
                {
                    var path = "src/Pages/Blocks/" + module.Definition.Component;
                    await WriteAsync(projectRoot, path + ".razor", module.Razor, ct);
                    await WriteAsync(projectRoot, path + ".razor.css", module.Css, ct);
                }
                await WriteAsync(projectRoot, "src/Pages/wwwroot/legacy/placeholder.svg", PlaceholderSvg, ct);
                await WriteAsync(projectRoot, "authoring.template.json", JsonSerializer.Serialize(definition, ContentJson.Options), ct);
                await WriteAsync(projectRoot, "legacy-provenance.json", JsonSerializer.Serialize(modules.Select(module => new
                {
                    module.SourceTemplateId, module.Definition.CatalogId, module.Definition.Title,
                    module.Definition.SourceLibrary, component = module.Definition.Component,
                    source = "Hyperpage built-in VisualPageTemplateDocument; generated with VisualPageGenerator"
                }), ContentJson.Options), ct);
                result.Add(await TemplateObjectLibrary.RegisterDesignGroupAsync(libraryRoot, projectRoot, modules.Key,
                    first.GroupTitle, "Portable elements, sections, and complete page patterns from the existing " + first.Definition.SourceLibrary + " library. Text and links remain editable in Hyperlib.",
                    "Hyperpage heritage", sourceTemplateId: null, ct: ct));
            }
            return result;
        }
        finally { SeedGate.Release(); }
    }

    /// <summary>Converts an owned authoring AST through the same portable, editable component pipeline as the heritage library.</summary>
    public static HyperlibLegacyTemplateModule ConvertForAuthoring(VisualPageTemplateDocument source, string additionalCode = "")
    {
        var catalog = new VisualPageComponentCatalog();
        return Convert(source, new(catalog, new(catalog)), catalog, additionalCode);
    }

    private static HyperlibLegacyTemplateModule Convert(VisualPageTemplateDocument source, VisualPageGenerator generator, VisualPageComponentCatalog catalog, string additionalCode = "")
    {
        var groupId = GroupForTemplate(source);
        var groupTitle = source.Library + " · Hyperpage collection";
        var slug = Regex.Replace(source.TemplateId, "[^a-zA-Z0-9]+", "-").ToLowerInvariant().Trim('-');
        var component = "Legacy" + string.Concat(slug.Split('-').Select(part => char.ToUpperInvariant(part[0]) + part[1..])) + "Block";
        var root = VisualPageJson.Clone(source.Root);
        if (root.DescriptorId != "core.page")
        {
            // A standalone list item, table row, or other constrained HTML element
            // needs its real semantic container in order to be a portable fragment.
            for (var depth = 0; depth < 8; depth++)
            {
                var descriptor = catalog.GetRequired(root.DescriptorId);
                var allowed = descriptor.Authoring.AllowedParentMarkupNames;
                if (allowed.Count == 0 || allowed.Contains("main", StringComparer.OrdinalIgnoreCase)) break;
                var parent = catalog.Descriptors.FirstOrDefault(candidate => allowed.Contains(candidate.MarkupName, StringComparer.OrdinalIgnoreCase) && candidate.AllowsChildren && !candidate.IsDocumentOnly)
                    ?? throw new ContentValidationException("The legacy Template '" + source.Name + "' needs an unsupported parent container.");
                var container = catalog.CreateElement(parent.Id);
                container.Children.Add(root);
                root = container;
            }
            var pageRoot = catalog.CreateElement("core.page");
            pageRoot.Name = source.Name + " surface";
            pageRoot.Children.Add(root);
            root = pageRoot;
        }
        var fields = new List<FieldDefinition>();
        var index = 0;
        foreach (var element in Flatten(root))
        {
            var ordinal = index++;
            element.ElementId = StableId(source.TemplateId + "/element/" + ordinal);
            // The source AST is preserved; deterministic identities keep generated CSS and catalog content stable.
            if (!string.IsNullOrWhiteSpace(element.Text))
            {
                var id = "text-" + ordinal;
                fields.Add(new() { Id = id, Title = FieldTitle(element.Name, "Text"), Type = element.Text.Contains('\n') || element.Text.Length > 100 ? "text" : "string", DefaultValue = ContentJson.Value(element.Text) });
                element.Bindings.Add(new() { BindingId = StableId(source.TemplateId + "/text/" + ordinal), Kind = VisualPageBindingKind.Text, Expression = "Context.Text(\"" + id + "\")" });
                element.Attributes["data-field"] = id;
            }
            foreach (var attribute in element.Attributes.ToList())
            {
                if (attribute.Key is "id" or "for" or "aria-describedby" or "aria-labelledby")
                {
                    element.Bindings.Add(new() { BindingId = StableId(source.TemplateId + "/attr/" + ordinal + "/" + attribute.Key), Kind = VisualPageBindingKind.Attribute,
                        Target = attribute.Key, Expression = "LocalIds(" + JsonSerializer.Serialize(attribute.Value) + ")" });
                    continue;
                }
                if (attribute.Key is not ("src" or "href" or "poster" or "alt" or "title" or "placeholder")) continue;
                var value = attribute.Value;
                if (attribute.Key is "src" or "poster" && (value.StartsWith("asset:", StringComparison.Ordinal) || value == "assets/placeholder.svg")) value = PlaceholderUrl;
                var isMedia = attribute.Key is "src" or "poster";
                var id = attribute.Key + "-" + ordinal;
                var fieldType = isMedia && TemplateValidator.IsSafeUrl(value) ? "media" : attribute.Key == "href" && TemplateValidator.IsSafeUrl(value) ? "url" : "string";
                fields.Add(new() { Id = id, Title = FieldTitle(element.Name, attribute.Key), Type = fieldType, DefaultValue = ContentJson.Value(value) });
                element.Bindings.Add(new() { BindingId = StableId(source.TemplateId + "/attr/" + ordinal + "/" + attribute.Key), Kind = VisualPageBindingKind.Attribute,
                    Target = attribute.Key, Expression = (isMedia ? "Asset" : attribute.Key == "href" ? "Link" : "") + (isMedia || attribute.Key == "href" ? "(" : "") + "Context.Text(\"" + id + "\")" + (isMedia || attribute.Key == "href" ? ")" : "") });
                // Keep a valid, local static fallback for the generator's schema check.
                if (isMedia) element.Attributes[attribute.Key] = value;
            }
        }
        if (fields.Count > 196) throw new ContentValidationException($"The legacy Template '{source.Name}' has too many editable fields to port as one component. Split it into smaller sections.");
        fields.AddRange(HyperlibTemplateEditSession.SupportedElementStylePresets());
        var page = new VisualPageDocument
        {
            ProjectId = StableId("legacy-project"), PageId = StableId(source.TemplateId + "/page"), ComponentName = component,
            Name = source.Name, Title = source.Name, Slug = slug, Route = "/legacy/" + slug, FileType = HyperpagePageFileType.RazorPage,
            Root = root
        };
        var project = new VisualPageProjectManifest { ProjectId = page.ProjectId, RootNamespace = "HyperLib.Work", Name = source.Name,
            Theme = new() { Tokens = new(source.ThemeTokens, StringComparer.Ordinal) } };
        var generated = generator.Generate(project, page);
        var razor = generated.Artifacts.Single(artifact => artifact.RelativePath.EndsWith(".razor", StringComparison.Ordinal)).Content;
        razor = string.Join('\n', razor.Replace("\r\n", "\n").Split('\n').Where(line => !line.StartsWith("@page ", StringComparison.Ordinal)
            && !line.StartsWith("<link rel=\"stylesheet\"", StringComparison.Ordinal) && !line.StartsWith("@* <auto-generated", StringComparison.Ordinal)));
        razor = razor.Replace("@namespace HyperLib.Work.Generated", "@namespace HyperLib.Work.Blocks", StringComparison.Ordinal);
        // Portable forms remain local demonstrations until a Work explicitly adds a submission handler.
        razor = Regex.Replace(razor, "<form(?=\\s|>)", "<form @onsubmit:preventDefault", RegexOptions.CultureInvariant);
        var code = "@* Source: " + source.TemplateId + " (" + source.Library + "). Editable fields preserve the original Hyperpage design. *@\n"
            + razor + "\n" + ComponentCode + "\n" + additionalCode;
        var css = generated.Artifacts.Single(artifact => artifact.RelativePath.EndsWith(".razor.css", StringComparison.Ordinal)).Content;
        var theme = generated.Artifacts.Single(artifact => artifact.RelativePath.EndsWith("visual-page-theme.css", StringComparison.Ordinal)).Content;
        // Scope old theme tokens to this component instead of changing the surrounding Work.
        css = theme.Replace(":root", ".vp-" + root.ElementId[..12], StringComparison.Ordinal) + "\n" + css;
        var category = source.Scope == "page" ? "Page layouts" : source.CategoryGroupLabel;
        var definition = new BlockDefinition
        {
            Id = "legacy-" + slug, Title = source.Name, Component = component, Fields = fields,
            CatalogId = "HLC-LEGACY-" + slug.ToUpperInvariant(), Category = category,
            Description = source.Description + " " + source.UsageHint,
            SourceTemplateId = source.TemplateId, SourceLibrary = source.Library
        };
        return new(groupId, groupTitle, source.TemplateId, definition, code, css);
    }

    private static string FieldTitle(string name, string suffix)
    {
        var title = name + " · " + suffix;
        return title.Length > 150 ? title[..150] : title;
    }
    internal static string GroupForLibrary(string library) => library switch
    {
        "Atlas Archive" => "legacy-atlas-archive", "Hyperpage starters" => "legacy-hyperpage-starters",
        "Application patterns" => HyperlibWorkProjectService.ApplicationTemplateGroup, _ => "legacy-core-essentials"
    };
    internal static string GroupForTemplate(VisualPageTemplateDocument source) => source.Library == "Application patterns"
        ? HyperlibWorkProjectService.ApplicationTemplateGroup + "-" + StudioTemplateRoleTaxonomy.Resolve(source).ToLowerInvariant()
        : GroupForLibrary(source.Library);
    private static IEnumerable<VisualPageElement> Flatten(VisualPageElement element)
    {
        yield return element;
        foreach (var child in element.Children) foreach (var descendant in Flatten(child)) yield return descendant;
    }
    private static string StableId(string value) => System.Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..32].ToLowerInvariant();
    private static async Task WriteAsync(string root, string relative, string text, CancellationToken ct)
    {
        var path = VisualPageProjectService.ResolveProjectPath(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, text, new UTF8Encoding(false), ct);
    }
    private const string ComponentCode = """
@code {
    [Parameter, EditorRequired] public HyperLib.Runtime.BlockContext Context { get; set; } = new();
    private string LocalIds(string value) => string.Join(" ", value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(id => Context.Id + "-" + id));
    private string Link(string value) => value.StartsWith('#') ? "#" + Context.Id + "-" + value[1..] : Asset(value);
    private static string Asset(string value) => value.StartsWith("/_content/", StringComparison.Ordinal) ? "." + value : HyperLib.Core.TemplateValidator.IsSafeUrl(value) ? value : "";
}
""";
    private const string PlaceholderSvg = """
<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="750" viewBox="0 0 1200 750"><defs><linearGradient id="sky" x2="1" y2="1"><stop stop-color="#172845"/><stop offset="1" stop-color="#668f9a"/></linearGradient></defs><rect width="1200" height="750" fill="url(#sky)"/><circle cx="875" cy="205" r="70" fill="#edcf85"/><path d="M0 610 300 260 560 555 775 385 1200 650V750H0Z" fill="#243c48"/><path d="M0 670 360 445 750 665 995 500 1200 620V750H0Z" fill="#376060"/><text x="60" y="690" font-family="system-ui,sans-serif" font-size="24" fill="#f7eedc">Replace with your own image</text></svg>
""";
}
