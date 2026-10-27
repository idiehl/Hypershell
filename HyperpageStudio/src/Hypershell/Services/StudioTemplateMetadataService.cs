using HyperLib.Core;
using Hypershell.Models;

namespace Hypershell.Services;

public sealed record StudioTemplateMetadata(string Id, string Name, string Type, string? Project, string? Category = null);

/// <summary>
/// Display metadata for reusable templates. Source identities, source labels and authored content are never rewritten.
/// Names are assigned against the complete source catalog, before either gallery applies a filter.
/// </summary>
public sealed class StudioTemplateMetadataService
{
    private readonly IReadOnlyDictionary<string, StudioTemplateMetadata> _metadata;
    private readonly Dictionary<string, VisualPageTemplateDocument> _legacy = new(StringComparer.Ordinal);

    // These are design families, not project names. Each describes the actual page arrangement.
    private static readonly IReadOnlyDictionary<string, string> Pages = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["architecture-studio"] = "Architectural case study", ["course-notebook"] = "Course lesson",
        ["documentation-desk"] = "Documentation guide", ["event-poster"] = "Event schedule",
        ["field-journal"] = "Illustrated journal", ["gallery-wall"] = "Exhibition gallery",
        ["insight-dashboard"] = "Metrics dashboard", ["maker-workbench"] = "Build guide",
        ["nonprofit-story"] = "Community project", ["podcast-studio"] = "Podcast episode",
        ["portfolio-grid"] = "Project portfolio", ["product-launch"] = "Product landing",
        ["recipe-kitchen"] = "Recipe", ["travel-atlas"] = "Travel itinerary",
        ["wave-laboratory"] = "Wave experiment", ["wellness-retreat"] = "Reflection journal",
        ["woodland-editorial"] = "Nature field notes",
        ["atlas-admin-interface"] = "Administration dashboard", ["atlas-public-archives"] = "Library reader",
        ["atlas-website-bio"] = "Biography", ["atlas-website-home"] = "Home and introduction",
        ["atlas-website-projects"] = "Project directory", ["atlas-website-services"] = "Services overview",
        ["atlas-website-tools"] = "Project planner"
    };

    private static readonly IReadOnlyDictionary<string, string> VisualNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["atlas.archive.page.longform.v1"] = "Long-form article", ["atlas.archive.page.history.v1"] = "Historical article",
        ["atlas.archive.page.language-culture.v1"] = "Language and culture article", ["atlas.archive.page.evolution.v1"] = "Evolution article",
        ["atlas.archive.page.coding.v1"] = "Programming guide", ["atlas.archive.page.media-dossier.v1"] = "Media dossier",
        ["atlas.hero.split.v1"] = "Split hero", ["atlas.card-grid.v1"] = "Card grid", ["atlas.cta.v1"] = "Call to action",
        ["atlas.archive.section.lead.v1"] = "Article introduction", ["atlas.archive.section.chapter.v1"] = "Article chapter",
        ["atlas.archive.section.timeline.v1"] = "Historical timeline", ["atlas.archive.section.gallery.v1"] = "Captioned image gallery",
        ["atlas.archive.section.audio.v1"] = "Audio and transcript", ["atlas.archive.section.video.v1"] = "Video feature",
        ["atlas.archive.section.data.v1"] = "Graph and data table", ["atlas.archive.section.sources.v1"] = "Sources and further reading",
        ["atlas.archive.element.heading.v1"] = "Kicker and heading", ["atlas.archive.element.card.v1"] = "Editorial card",
        ["atlas.archive.element.callout.v1"] = "Fact callout", ["atlas.archive.element.figure.v1"] = "Image and caption",
        ["atlas.archive.element.quote.v1"] = "Pull quote", ["atlas.archive.element.glossary.v1"] = "Glossary entry",
        ["atlas.archive.element.code.v1"] = "Code listing", ["atlas.archive.element.citation.v1"] = "Source citation",
        ["atlas.archive.element.divider.v1"] = "Section divider"
    };

    private static readonly IReadOnlyDictionary<string, string> WebsiteModules = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["navigation"] = "Navigation", ["hero"] = "Celestial hero", ["starfield"] = "Animated starfield",
        ["map-grid"] = "Cartographic backdrop", ["emblem"] = "Emblem", ["section-heading"] = "Editorial heading",
        ["principles"] = "Principles grid", ["principle-card"] = "Principle card", ["services"] = "Services grid",
        ["service-card"] = "Service card", ["action-banner"] = "Action banner", ["button-bar"] = "Action pair", ["footer"] = "Footer",
        ["portrait"] = "Portrait introduction", ["timeline"] = "Biography timeline", ["timeline-entry"] = "Timeline entry",
        ["social-links"] = "Contact links", ["project-card"] = "Project card", ["project-grid"] = "Project grid", ["command-block"] = "Copyable command",
        ["breadcrumbs"] = "Breadcrumb trail", ["search-tree"] = "Searchable hierarchy", ["collection-card"] = "Collection card",
        ["collection-grid"] = "Collection grid", ["reader-heading"] = "Reader introduction", ["reader-workspace"] = "Reader workspace",
        ["admin-header"] = "Administration header", ["status-strip"] = "Status strip", ["metric"] = "Metric panel",
        ["module-card"] = "Module launcher", ["inventory"] = "Editable inventory", ["inventory-row"] = "Inventory row", ["notice"] = "Notice",
        ["empty-state"] = "Empty state", ["login-form"] = "Sign-in form", ["code-entry"] = "One-time code entry", ["approval"] = "Decision panel",
        ["inline-editor"] = "Expandable editor", ["download-card"] = "Release card", ["scope-planner"] = "Launch scope planner",
        ["risk-badge"] = "Risk status", ["result-summary"] = "Result summary", ["form-field"] = "Labeled input", ["contact-form"] = "Inquiry draft"
    };

    // Configured examples are named for their reusable role, rather than their demonstration copy.
    private static readonly IReadOnlyDictionary<string, string> Examples = BuildExamples();
    private static readonly IReadOnlyDictionary<string, string> Palettes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["architecture-studio"] = "Stone and brown", ["blank"] = "White and blue", ["course-notebook"] = "Lavender and violet",
        ["documentation-desk"] = "Gray and blue", ["event-poster"] = "Plum and gold", ["field-journal"] = "Cream and rust",
        ["gallery-wall"] = "Ivory and taupe", ["insight-dashboard"] = "Charcoal and cyan", ["maker-workbench"] = "Charcoal and amber",
        ["nonprofit-story"] = "Cream and green", ["podcast-studio"] = "Plum and pink", ["portfolio-grid"] = "Gray and vermilion",
        ["product-launch"] = "Navy and pale blue", ["recipe-kitchen"] = "Cream and terracotta", ["travel-atlas"] = "Mist and teal",
        ["wave-laboratory"] = "Charcoal and blue", ["wellness-retreat"] = "Blush and rose", ["woodland-editorial"] = "Cream and forest green",
        ["work-landing-editorial"] = "Ivory and rust", ["work-landing-directory"] = "Mist and forest green",
        ["work-landing-handbook"] = "Blue gray and blue", ["work-landing-gallery"] = "Ivory and brown",
        ["work-landing-course"] = "Lavender and violet", ["work-landing-journal"] = "Cream and rust", ["work-landing-minimal"] = "White and charcoal"
    };

    public StudioTemplateMetadataService(HyperlibWorkProjectService? source, IReadOnlyList<VisualPageTemplateDocument> visualTemplates)
    {
        var seeds = new List<StudioTemplateMetadata>();
        foreach (var visual in visualTemplates)
        {
            var legacyGroup = HyperlibLegacyTemplateLibraryService.GroupForTemplate(visual);
            var slug = System.Text.RegularExpressions.Regex.Replace(visual.TemplateId, "[^a-zA-Z0-9]+", "-").ToLowerInvariant().Trim('-');
            _legacy[legacyGroup + ":element-legacy-" + slug] = visual;
            seeds.Add(new("visual:" + visual.TemplateId,
                visual.StorageScope == "built-in" && VisualNames.TryGetValue(visual.TemplateId, out var name) ? name : visual.Name,
                TypeFor(visual.Scope == "element" ? "module" : visual.Scope), visual.StorageScope == "built-in" && visual.TemplateId.StartsWith("atlas.", StringComparison.Ordinal) ? "Atlas" : null,
                StudioTemplateRoleTaxonomy.Resolve(visual)));
        }
        foreach (var (section, presets) in new[] { ("header", HyperlibHeaderLayouts.All), ("navigation", HyperlibNavigationLayouts.All),
            ("body", HyperlibBodyLayouts.All), ("footer", HyperlibFooterLayouts.All) })
            foreach (var preset in presets)
            {
                var name = preset.Title;
                if (!name.Contains(section, StringComparison.OrdinalIgnoreCase)) name += " " + section;
                seeds.Add(new("section:" + section + ":" + preset.Id, name, "Section", null, section == "body" ? "Layout" : "Navigation"));
            }
        if (source is not null)
        {
            var catalog = source.GetCatalog();
            foreach (var family in catalog.GroupBy(item => item.GroupId, StringComparer.Ordinal))
            {
                var known = Pages.ContainsKey(family.Key) || WorkLandingTemplateCatalog.Contains(family.Key) || family.Key == "blank" || family.Key.StartsWith("foundation-", StringComparison.Ordinal)
                    || family.Key.StartsWith("legacy-", StringComparison.Ordinal);
                var definition = known ? source.GetCatalogObject(family.Key, family.First().ObjectId).Definition : null;
                foreach (var item in family.Where(item => item.Kind != "region"))
                {
                    if (!known || _legacy.ContainsKey(item.Id)) continue;
                    var name = item.Title;
                    name = item.Kind switch
                    {
                        "layout" when item.ObjectId == "page-layout" => Pages.GetValueOrDefault(item.GroupId, item.GroupTitle),
                        "element" when ProjectForFamily(item.GroupId) == "Atlas" && WebsiteModules.TryGetValue(item.ObjectId[8..], out var module) => module,
                        "preset" when Examples.TryGetValue(item.Id, out var example) => example,
                        "palette" => Palette(item.GroupId) + " palette",
                        "typography" when item.ObjectId == "typography" => Typography(definition!) + " typography",
                        "style" => Pages.GetValueOrDefault(item.GroupId, FamilyName(item.GroupId)) + " style",
                        _ => name
                    };
                    seeds.Add(new("object:" + item.Id, name, TypeFor(CatalogScope(item)), ProjectForFamily(item.GroupId),
                        StudioTemplateRoleTaxonomy.Resolve(item.Id, name, item.Category, CatalogScope(item), item.Kind)));
                }
            }
        }
        _metadata = AssignNames(seeds).ToDictionary(item => item.Id, StringComparer.Ordinal);
    }

    public StudioTemplateMetadata Describe(string id, string sourceTitle, string scope, string? project = null)
    {
        var canonical = CanonicalId(id);
        return _metadata.TryGetValue(canonical, out var metadata) ? metadata with { Id = id, Type = TypeFor(scope) }
            : new(id, sourceTitle, TypeFor(scope), project, StudioTemplateRoleTaxonomy.Resolve(id, sourceTitle, "", scope, ""));
    }

    public string CatalogScope(HyperlibWorkTemplateChoice choice) => _legacy.TryGetValue(choice.Id, out var visual)
        ? visual.Scope == "element" ? "module" : visual.Scope
        : choice.Kind == "layout" && choice.ObjectId == "page-layout" ? "page" : "module";

    public string? LegacyTemplateId(HyperlibWorkTemplateChoice choice) => _legacy.TryGetValue(choice.Id, out var visual) ? visual.TemplateId : null;

    /// <summary>Distinct source identities receive stable ordinal suffixes; aliases must use their canonical identity first.</summary>
    public static IReadOnlyList<StudioTemplateMetadata> AssignNames(IEnumerable<StudioTemplateMetadata> source)
    {
        var unique = source.GroupBy(item => item.Id, StringComparer.Ordinal).Select(group => group.First()).ToArray();
        var result = new List<StudioTemplateMetadata>();
        foreach (var scope in unique.GroupBy(item => (item.Type, item.Project)))
        {
            // Keep a naturally numbered name free for its own family (for example, a genuine "Heading 2").
            var reserved = scope.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in scope.GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var ordered = group.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
                var suffix = 2;
                for (var index = 0; index < ordered.Length; index++)
                {
                    var name = ordered[index].Name;
                    if (index > 0)
                        do { name = ordered[index].Name + " " + suffix++; } while (reserved.Contains(name) || used.Contains(name));
                    used.Add(name);
                    result.Add(ordered[index] with { Name = name });
                }
            }
        }
        return result.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
    }

    public static string TypeFor(string scope) => scope switch { "page" => "Page", "section" => "Section", "module" => "Module", _ => "Element" };
    public static string? ProjectForFamily(string id) => id is "atlas-admin-interface" or "atlas-public-archives"
        or "atlas-website-bio" or "atlas-website-home" or "atlas-website-projects" or "atlas-website-services" or "atlas-website-tools"
        or "legacy-atlas-archive" || WorkLandingTemplateCatalog.Project(id) == "Atlas" ? "Atlas" : null;

    private string CanonicalId(string id)
    {
        if (id.StartsWith("page:", StringComparison.Ordinal)) id = id[5..];
        if (id.StartsWith("object:", StringComparison.Ordinal) && _legacy.TryGetValue(id[7..], out var visual)) return "visual:" + visual.TemplateId;
        if (!id.StartsWith("hyperlib:", StringComparison.Ordinal)) return id;
        var parts = id[9..].Split('/');
        return "object:" + parts[0] + ":" + (parts.Length == 1 ? "page-layout" : parts[1]);
    }
    private static string Palette(string family) => ProjectForFamily(family) == "Atlas" && !family.StartsWith("legacy-", StringComparison.Ordinal)
        ? "Navy and gold" : Palettes.GetValueOrDefault(family, family.StartsWith("foundation-", StringComparison.Ordinal) ? "Cream and forest green" : "White and blue");
    private static string FamilyName(string family) => family switch
    {
        _ when WorkLandingTemplateCatalog.Contains(family) => WorkLandingTemplateCatalog.List().Single(item => item.Id == family).Title,
        "blank" => "Basic page", "legacy-atlas-archive" or "legacy-core-essentials" or "legacy-hyperpage-starters" => "Basic page",
        _ when family.StartsWith("foundation-", StringComparison.Ordinal) => "Basic page", _ => family
    };
    private static string Typography(TemplateDefinition definition)
    {
        string Font(string id) => definition.Appearance.FirstOrDefault(field => field.Id == id)?.DefaultValue?.GetString() switch
        { "serif" => "Serif", "monospace" => "Monospace", "humanist" => "Humanist", "geometric" => "Geometric", _ => "System" };
        var body = Font("font"); var heading = Font("headingFont");
        return body == heading ? body : body + " and " + heading.ToLowerInvariant();
    }
    private static IReadOnlyDictionary<string, string> BuildExamples()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        void Family(string id, params (string Id, string Name)[] items)
        { foreach (var item in items) result.Add(id + ":example-" + item.Id, item.Name); }
        Family("architecture-studio", ("project-header", "Header"), ("project-hero", "Project image"), ("project-story", "Project narrative"),
            ("project-facts", "Project notes"), ("specifications", "Section divider"), ("light", "Design principle"), ("material", "Design principle"),
            ("threshold", "Design principle"), ("project-footer", "Footer"));
        Family("course-notebook", ("lesson-header", "Header"), ("learning-goal", "Learning goal"), ("duration", "Lesson duration"),
            ("lesson", "Lesson text"), ("exercise", "Practice exercise"), ("worked-example", "Worked example"), ("reflection-heading", "Section divider"),
            ("reflection", "Reflection form"), ("lesson-footer", "Footer"));
        Family("documentation-desk", ("guide-title", "Header"), ("overview", "Contract summary"), ("prerequisites", "Prerequisites"),
            ("quick-start", "Quick-start instructions"), ("review", "Review checklist"), ("reference-label", "Section divider"), ("reference", "Field reference"), ("guide-footer", "Footer"));
        Family("event-poster", ("poster", "Event banner"), ("schedule-heading", "Section divider"), ("doors", "Schedule item"), ("workshop", "Schedule item"),
            ("share", "Schedule item"), ("venue", "Venue map"), ("access", "Arrival information"), ("registration", "Registration form"), ("event-footer", "Footer"));
        Family("field-journal", ("masthead", "Header"), ("opening-image", "Article image"), ("essay", "Essay text"), ("margin-note", "Margin note"),
            ("second-column", "Article column"), ("reading-column", "Reading column"), ("end-mark", "Section divider"), ("colophon", "Footer"));
        Family("gallery-wall", ("exhibition-label", "Header"), ("pair", "Paired image gallery"), ("work-label", "Artwork label"), ("artist-note", "Artist statement"),
            ("pause", "Section divider"), ("final-study", "Artwork image"), ("gallery-footer", "Footer"));
        Family("insight-dashboard", ("dashboard-header", "Header"), ("metric-projects", "Project counter"), ("metric-finished", "Completion counter"),
            ("metric-experiments", "Experiment counter"), ("progress", "Progress note"), ("attention", "Attention note"), ("planning-heading", "Section divider"),
            ("weekly-checklist", "Weekly checklist"), ("focus", "Focus timer"), ("dashboard-footer", "Footer"));
        Family("maker-workbench", ("build-header", "Header"), ("build-plan", "Build plan image"), ("materials", "Materials list"), ("build-steps", "Build instructions"),
            ("measurements", "Measurement tool"), ("build-checklist", "Build checklist"), ("build-log", "Build log note"), ("workbench-footer", "Footer"));
        Family("nonprofit-story", ("story-hero", "Community project banner"), ("impact-one", "Impact card"), ("impact-two", "Impact card"), ("impact-three", "Impact card"),
            ("story", "Community project story"), ("invitation", "Participation invitation"), ("project-map", "Project location map"), ("volunteer", "Volunteer form"), ("community-footer", "Footer"));
        Family("podcast-studio", ("episode-art", "Episode artwork"), ("episode-title", "Header"), ("episode-audio", "Episode audio"), ("show-notes", "Show notes"),
            ("episode-credit", "Episode credits"), ("transcript-label", "Section divider"), ("transcript", "Transcript"), ("podcast-footer", "Footer"));
        Family("portfolio-grid", ("intro", "Header"), ("project-one", "Project banner"), ("project-two", "Project banner"), ("capabilities", "Section divider"),
            ("research", "Capability card"), ("design", "Capability card"), ("build", "Capability card"), ("contact", "Contact form"), ("closing", "Footer"));
        Family("product-launch", ("product-hero", "Product banner"), ("benefit-focus", "Benefit card"), ("benefit-flex", "Benefit card"), ("benefit-local", "Benefit card"),
            ("demo-heading", "Section divider"), ("product-demo", "Product walkthrough"), ("product-details", "Product details"), ("interest", "Interest form"), ("product-footer", "Footer"));
        Family("recipe-kitchen", ("recipe-header", "Header"), ("recipe-photo", "Recipe image"), ("ingredients", "Ingredients list"), ("method", "Recipe instructions"),
            ("kitchen-timer", "Kitchen timer"), ("cook-note", "Cooking note"), ("serving", "Section divider"), ("recipe-footer", "Footer"));
        Family("travel-atlas", ("destination", "Destination banner"), ("city-map", "Destination map"), ("pocket-guide", "Packing list"), ("morning", "Morning itinerary"),
            ("afternoon", "Afternoon itinerary"), ("route", "Route checklist"), ("travel-footer", "Footer"));
        Family("wave-laboratory", ("page-header", "Header"), ("wave-intro", "Wave introduction"), ("wave-experiment", "Wave controls"), ("wave-note", "Experiment instructions"),
            ("wave-audio", "Audio example"), ("wave-video", "Video example"), ("wave-citation", "Wave reference"), ("page-divider", "Section divider"), ("page-footer", "Footer"));
        Family("wellness-retreat", ("welcome", "Header"), ("quiet-image", "Reflection image"), ("reflection", "Reflection text"), ("pause-timer", "Pause timer"),
            ("breathing-room", "Section divider"), ("gentle-note", "Reflection note"), ("quiet-footer", "Footer"));
        Family("woodland-editorial", ("page-header", "Header"), ("woodland-intro", "Field introduction"), ("woodland-image", "Habitat image"), ("woodland-note", "Observation instructions"),
            ("woodland-counter", "Observation counter"), ("woodland-richtext", "Field notes"), ("woodland-gallery", "Observation gallery"), ("page-divider", "Section divider"), ("page-footer", "Footer"));
        Family("atlas-admin-interface", ("atlas-section-0", "Administration header"), ("atlas-section-1", "Status strip"), ("atlas-section-2", "Collection card"),
            ("atlas-section-3", "Workspace inventory"), ("atlas-section-4", "Status notice"), ("atlas-section-5", "Footer"));
        Family("atlas-public-archives", ("atlas-section-0", "Navigation"), ("atlas-section-1", "Breadcrumbs"), ("atlas-section-2", "Reader workspace"), ("atlas-section-3", "Footer"));
        Family("atlas-website-bio", ("atlas-section-0", "Navigation"), ("atlas-section-1", "Portrait and introduction"), ("atlas-section-2", "Experience timeline"),
            ("atlas-section-3", "Social links"), ("atlas-section-4", "Footer"));
        Family("atlas-website-home", ("atlas-section-0", "Navigation"), ("atlas-section-1", "Celestial hero"), ("atlas-section-2", "Principles overview"), ("atlas-section-3", "Footer"));
        Family("atlas-website-projects", ("atlas-section-0", "Navigation"), ("atlas-section-1", "Section heading"), ("atlas-section-2", "Project grid"),
            ("atlas-section-3", "Command instructions"), ("atlas-section-4", "Footer"));
        Family("atlas-website-services", ("atlas-section-0", "Navigation"), ("atlas-section-1", "Section heading"), ("atlas-section-2", "Service cards"),
            ("atlas-section-3", "Action banner"), ("atlas-section-4", "Footer"));
        Family("atlas-website-tools", ("atlas-section-0", "Navigation"), ("atlas-section-1", "Scope planner"), ("atlas-section-2", "Footer"));
        return result;
    }
}
