using System.IO;
using System.Text.RegularExpressions;
using HyperLib.Core;
using Hypershell.Models;

namespace Hypershell.Services;

public sealed record StudioCompleteTemplateChoice(string Id, string Title, string Description,
    string Category, string Collection, bool IsBuiltIn, string Background, string Accent,
    IReadOnlyList<StudioProjectKind> SupportedKinds, string? Type = null, string? Project = null, string? SourceTitle = null);

/// <summary>Read-only complete-page gallery. Object shopping remains a separate scratch workflow.</summary>
public sealed class StudioCompleteTemplateService
{
    private readonly HyperlibWorkProjectService? _work;
    private readonly VisualPageTemplateService _visual;
    private IReadOnlyList<VisualPageTemplateDocument>? _visualTemplates;
    private IReadOnlyList<StudioCompleteTemplateChoice>? _choices;
    private StudioTemplateMetadataService? _metadata;

    public StudioCompleteTemplateService(string? libraryRoot = null, string? personalTemplateRoot = null)
    {
        _visual = new(new VisualPageComponentCatalog(), personalTemplateRoot);
        try { _work = new(libraryRoot); }
        catch (Exception exception) when (libraryRoot is null && (exception is IOException or InvalidDataException or UnauthorizedAccessException)) { }
    }

    public string? TemplateLibraryRoot => _work?.TemplateLibraryRoot;
    internal StudioTemplateMetadataService Metadata => _metadata ??= new(_work, _visual.BuiltInTemplates);

    public IReadOnlyList<StudioCompleteTemplateChoice> List()
    {
        if (_choices is not null) return _choices;
        var choices = _visual.BuiltInTemplates.Where(item => item.Scope == "page")
            .Select(item => new StudioCompleteTemplateChoice("visual:" + item.TemplateId, item.Name,
                item.Description, item.Category, item.Library, item.StorageScope == "built-in", VisualColor(item, "background", "#101820"),
                VisualColor(item, "accent", "#65a88e"), item.StorageScope == "built-in" ? [StudioProjectKind.Hyperlib, StudioProjectKind.Razor, StudioProjectKind.Html] : [StudioProjectKind.Razor, StudioProjectKind.Html])).ToList();
        if (_work is not null)
        {
            var catalog = _work.GetCatalog();
            var originals = ReusableTemplateCatalog.List(_work.TemplateLibraryRoot);
            foreach (var item in catalog.Where(item => item.Kind == "layout" && item.GroupId != ReusableTemplateCatalog.BlankId))
            {
                var source = _work.GetCatalogObject(item.GroupId, item.ObjectId);
                if (source.Blocks.Count == 0) continue;
                var style = source.Definition.Appearance;
                string Color(string id, string fallback) => style.LastOrDefault(field => field.Id == id)?.DefaultValue?.GetString() is { } value
                    && Regex.IsMatch(value, "^#[a-fA-F0-9]{6}$", RegexOptions.CultureInvariant) ? value : fallback;
                var original = originals.SingleOrDefault(value => value.Id == item.GroupId);
                choices.Add(new("hyperlib:" + item.GroupId + (item.ObjectId == "page-layout" ? "" : "/" + item.ObjectId),
                    item.ObjectId == "page-layout" ? item.GroupTitle : item.Title, item.Description, original?.Category ?? item.Category,
                    original is null ? "Core Template Library" : original.IsBuiltIn ? "Hyperlib collection" : "My templates",
                    original?.IsBuiltIn ?? false, Color("background", "#101820"), Color("accent", "#65a88e"), [StudioProjectKind.Hyperlib]));
            }
        }
        return _choices = choices.Select(item =>
        {
            var metadata = Metadata.Describe(item.Id, item.Title, "page");
            return item with { Title = metadata.Name, Type = metadata.Type, Project = metadata.Project, SourceTitle = item.Title,
                Category = metadata.Category ?? StudioTemplateRoleTaxonomy.Resolve(item.Id, metadata.Name, item.Category, "page", "layout") };
        }).OrderBy(item => item.Collection, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public VisualPageTemplateDocument GetVisualTemplate(string id)
    {
        if (!id.StartsWith("visual:", StringComparison.Ordinal)) throw new InvalidDataException("Choose a Hyperpage page template for this project type.");
        var original = VisualTemplates().SingleOrDefault(item => item.Scope == "page" && item.TemplateId == id[7..])
            ?? throw new InvalidDataException("This complete page template is no longer available. Refresh the gallery.");
        var clone = VisualPageJson.Clone(original);
        clone.SourcePath = original.SourcePath;
        return clone;
    }

    public HyperlibWorkProjectDefinition BuildDefinition(string id, string title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200 || title.Any(char.IsControl)) throw new InvalidDataException("Name the project with 1–200 printable characters.");
        string choiceId;
        string? appearanceId = null;
        string background = "#101820", accent = "#65a88e";
        if (id.StartsWith("visual:", StringComparison.Ordinal))
        {
            var visual = GetVisualTemplate(id);
            if (visual.StorageScope != "built-in") throw new InvalidDataException("Personal Hyperpage templates can currently create Razor and HTML projects. Choose a Hyperlib template for this Work.");
            var group = HyperlibLegacyTemplateLibraryService.GroupForTemplate(visual);
            var slug = Regex.Replace(visual.TemplateId, "[^a-zA-Z0-9]+", "-").ToLowerInvariant().Trim('-');
            choiceId = group + ":element-legacy-" + slug;
            background = VisualColor(visual, "background", background); accent = VisualColor(visual, "accent", accent);
        }
        else if (id.StartsWith("hyperlib:", StringComparison.Ordinal))
        {
            if (_work is null) throw new InvalidDataException("The Hyperlib template library is unavailable. Choose its library location before creating this project.");
            var parts = id[9..].Split('/');
            if (parts.Length is < 1 or > 2) throw new InvalidDataException("The complete template identity is invalid.");
            choiceId = parts[0] + ":" + (parts.Length == 1 ? "page-layout" : parts[1]);
            var catalog = _work.GetCatalog();
            var selected = catalog.SingleOrDefault(item => item.Id == choiceId && item.Kind == "layout")
                ?? throw new InvalidDataException("This complete page template is no longer available. Refresh the gallery.");
            if (_work.GetCatalogObject(selected.GroupId, selected.ObjectId).Blocks.Count == 0) throw new InvalidDataException("This template does not contain a complete page layout.");
            appearanceId = catalog.FirstOrDefault(item => item.GroupId == selected.GroupId && item.Kind == "style")?.Id;
            var metadata = List().Single(item => item.Id == id); background = metadata.Background; accent = metadata.Accent;
        }
        else throw new InvalidDataException("Choose a complete page template from the gallery.");
        var recipe = new HyperlibPageTemplateDefinition { CompleteTemplateObjectId = choiceId, CompleteTemplateAppearanceId = appearanceId,
            BackgroundColor = background, AccentColor = accent, IncludeNavigation = false };
        var page = ContentJson.Clone(recipe);
        return new() { PipelineVersion = 2, WorkTitle = title.Trim(), Description = "", PageDefinitions = [],
            SelectedToolsetIds = new[] { choiceId, appearanceId }.Where(value => !string.IsNullOrEmpty(value)).Select(value => value!).ToList(),
            LandingTemplate = recipe, PageTemplate = page, DefaultPageTemplateId = "page-default",
            PageTemplates = [new() { Id = "page-default", Name = "Content Page", Definition = page }] };
    }

    private static string VisualColor(VisualPageTemplateDocument template, string purpose, string fallback)
    {
        foreach (var name in purpose == "background" ? new[] { "color.background", "page-bg", "page-background", "background", "surface", "bg" } : new[] { "color.accent", "accent", "primary", "accent-color" })
            if (template.ThemeTokens.TryGetValue(name, out var value) && Regex.IsMatch(value, "^#[a-fA-F0-9]{6}$", RegexOptions.CultureInvariant)) return value;
        return fallback;
    }

    private IReadOnlyList<VisualPageTemplateDocument> VisualTemplates()
        // The synchronous gallery API must never wait on a WPF-dispatcher-capturing asynchronous file read.
        // The existing validated reader bounds files/assets and rejects unsafe paths; it runs on its own pool context.
        => _visualTemplates ??= _visual.BuiltInTemplates.Concat(Task.Run(() => _visual.LoadPersonalAsync()).GetAwaiter().GetResult().Templates
            .Where(item => !_visual.BuiltInTemplates.Any(builtIn => builtIn.TemplateId == item.TemplateId)))
            .GroupBy(item => item.TemplateId, StringComparer.Ordinal).Select(group => group.First()).ToArray();
}
