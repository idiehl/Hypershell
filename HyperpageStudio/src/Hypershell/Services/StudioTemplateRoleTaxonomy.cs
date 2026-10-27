namespace Hypershell.Services;

/// <summary>Application responsibilities, independent of a template's visual treatment or provenance.</summary>
public static class StudioTemplateRoleTaxonomy
{
    public static IReadOnlyList<string> Categories { get; } =
        ["Navigation", "Media", "Grid", "Data", "Graphs", "Forms", "Content", "Search", "Commerce", "People", "Feedback", "Workflow", "Layout", "Appearance"];

    public static string Resolve(VisualPageTemplateDocument template)
    {
        if (template.Library == "Application patterns" && Categories.Contains(template.Category)) return template.Category;
        return Resolve(template.TemplateId, template.Name, template.Category, template.Scope, "");
    }

    public static string Resolve(string id, string name, string category, string scope, string kind)
    {
        if (kind is "palette" or "typography" or "style") return "Appearance";
        var words = (id + " " + name).ToLowerInvariant().Replace('-', ' ').Replace('_', ' ').Replace('.', ' ');
        bool Has(params string[] terms) => terms.Any(term => words.Contains(term, StringComparison.Ordinal));
        // Prefer the concrete object's job over its source family's name.
        var label = name.ToLowerInvariant();
        bool Named(params string[] terms) => terms.Any(term => System.Text.RegularExpressions.Regex.IsMatch(label,
            @"(?<!\w)" + System.Text.RegularExpressions.Regex.Escape(term) + @"s?(?!\w)",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant));
        if (Named("navigation", "breadcrumb", "pagination", "footer", "header", "masthead", "contents", "landing", "home and introduction", "menu", "table of contents")) return "Navigation";
        if (Named("graph", "chart", "sparkline", "histogram", "scatter", "plot", "heatmap")) return "Graphs";
        if (Named("image", "gallery", "video", "audio", "media", "podcast", "portrait", "banner", "artwork", "photo", "map")) return "Media";
        if (Named("form", "input", "contact", "registration", "inquiry", "subscribe", "sign in", "login", "account", "settings", "checkbox", "validation")) return "Forms";
        if (Named("search", "filter", "directory", "library reader", "index", "resources")) return "Search";
        if (Named("price", "pricing", "checkout", "cart", "product", "service", "payment", "invoice")) return "Commerce";
        if (Named("grid", "portfolio", "collection card", "cards", "columns", "exhibition")) return "Grid";
        if (Named("metric", "statistic", "dashboard", "counter", "table", "data", "inventory", "facts", "reference", "result", "summary", "metadata", "glossary")) return "Data";
        if (Named("profile", "person", "people", "team", "author", "biography", "testimonial", "credits")) return "People";
        if (Named("status", "notice", "alert", "callout", "notification", "progress", "empty state", "attention", "error", "confirmation")) return "Feedback";
        if (Named("checklist", "timeline", "schedule", "steps", "planner", "itinerary", "timer", "experiment", "tool", "exercise", "task", "command", "workflow")) return "Workflow";
        if (Named("layout", "container", "divider", "separator", "spacer", "split", "box", "panel", "section")) return "Layout";
        if (Named("color", "palette", "typography", "font", "background", "surface", "style", "starfield")) return "Appearance";
        var sourceCategory = category.ToLowerInvariant();
        if (sourceCategory.Contains("media")) return "Media";
        if (sourceCategory.Contains("navigation")) return "Navigation";
        if (sourceCategory.Contains("form") || sourceCategory.Contains("input")) return "Forms";
        if (sourceCategory.Contains("data") || sourceCategory.Contains("table")) return "Data";
        if (sourceCategory.Contains("grid")) return "Grid";
        if (sourceCategory.Contains("layout") || sourceCategory.Contains("structure")) return "Layout";
        if (Has("section:header", "section:navigation", "section:footer")) return "Navigation";
        return "Content";
    }
}
