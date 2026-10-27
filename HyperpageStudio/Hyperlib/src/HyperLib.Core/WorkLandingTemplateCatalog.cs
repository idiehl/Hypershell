namespace HyperLib.Core;

/// <summary>Complete Work entrances whose contents are resolved from the reader's current Work.</summary>
public static partial class WorkLandingTemplateCatalog
{
    public const string Category = "Landing page";
    private static readonly BaseTemplateInfo[] Entries =
    [
        Info("work-landing-editorial", "Editorial contents landing", "An oversized editorial opening with a numbered reading list and generous margins.", "#f8f5ed", "#82523b", "#272923"),
        Info("work-landing-directory", "Card directory landing", "A clear introduction and responsive directory cards for every Page in the Work.", "#eff4f2", "#246854", "#18372e"),
        Info("work-landing-handbook", "Handbook landing", "A practical guide entrance with a compact contents rail and a direct start-reading action.", "#f0f3f7", "#235f93", "#162d40"),
        Info("work-landing-gallery", "Gallery index landing", "A quiet exhibition-style title, graphic cover study, and a two-column Page index.", "#faf7f1", "#8c654b", "#342e2a"),
        Info("work-landing-course", "Course contents landing", "A warm learning hub with numbered lesson cards, a clear reading order, and room for an introduction.", "#f6f2ff", "#654297", "#302241"),
        Info("work-landing-journal", "Journal cover landing", "A literary cover with a large typographic initial and an elegant issue-style table of contents.", "#fbf0dc", "#8f432a", "#3c2a22"),
        Info("work-landing-minimal", "Minimal reading landing", "A restrained monochrome entrance with concise navigation and a compact linked Page list.", "#ffffff", "#232e39", "#17212b"),
        Info("atlas-work-landing-observatory", "Starfield contents landing", "Atlas Universalis branding, a deep navy starfield, a gold orbital emblem, and a live numbered contents list.", "#070d1a", "#c8963e", "#e9edf4"),
        Info("atlas-work-landing-atlas", "Cartographic directory landing", "Atlas Universalis branding with a fine celestial grid, a split hero, and a responsive Page directory.", "#0b1726", "#d4b06a", "#eef2f7"),
        Info("atlas-work-landing-archive", "Archive reading landing", "Atlas Universalis branding, a restrained navy-and-gold reading room, and an ordered archive of Pages.", "#09111d", "#cda85a", "#edf0f5")
    ];

    public static IReadOnlyList<BaseTemplateInfo> List() => ContentJson.Clone(Entries);
    public static bool Contains(string? id) => Entries.Any(item => item.Id == id);
    public static string? Project(string id) => Contains(id) && id.StartsWith("atlas-", StringComparison.Ordinal) ? "Atlas" : null;

    public static TemplateDefinition Create(string id)
    {
        var info = Entries.SingleOrDefault(item => item.Id == id)
            ?? throw new ContentValidationException("Choose a listed Work landing template.");
        var template = StarterTemplate.Create();
        template.TemplateId = id;
        template.Title = info.Title;
        template.EmptyPageDefault = false;
        var atlas = Project(id) == "Atlas";
        var values = new Dictionary<string, string>
        {
            ["variant"] = id, ["background"] = info.Background, ["accent"] = info.Accent, ["ink"] = info.Ink,
            ["surface"] = atlas ? "#102033" : "#ffffff", ["muted"] = atlas ? "#a7b9cc" : "#626c70",
            ["font"] = "system", ["headingFont"] = id.Contains("handbook", StringComparison.Ordinal) || id.Contains("minimal", StringComparison.Ordinal) ? "system" : "serif"
        };
        foreach (var field in template.Appearance)
            if (values.TryGetValue(field.Id, out var value)) field.DefaultValue = ContentJson.Value(value);
        // These families contribute complete entrances; primitive Text/Image/etc. remain in the existing Elements library.
        template.Blocks.Clear();
        template.Regions = [new() { Id = "main", Title = "Work entrance" }];
        template.Blocks.Add(new()
        {
            Id = "work-landing", Title = "Work landing", Component = "WorkLandingBlock", Category = Category,
            Description = "An editable Work entrance. Its navigation and contents always link to this Work's current Pages.",
            Fields =
            [
                Field("title", "Title (empty uses Work name)", "string", ""),
                Field("brand", "Site branding (empty uses Work name)", "string", atlas ? "ATLAS UNIVERSALIS" : ""),
                Field("eyebrow", "Eyebrow", "string", atlas ? "ATLAS UNIVERSALIS / A WORK TO EXPLORE" : Eyebrow(id)),
                Field("text", "Introduction", "text", Introduction(id)),
                Field("contentsTitle", "Contents heading", "string", id.EndsWith("course", StringComparison.Ordinal) ? "Your learning path" : "Explore this Work"),
                Field("startLabel", "Start reading label", "string", id.EndsWith("course", StringComparison.Ordinal) ? "Begin the first lesson" : "Start reading"),
                Field("footerText", "Footer", "string", atlas ? "ATLAS UNIVERSALIS · Ideas worth connecting." : "A collection of ideas, made to be explored."),
                Field("cover", "Cover image (optional)", "media", ""), Field("coverAlt", "Cover description", "string", ""),
                new() { Id = "design", Title = "Design", Type = "select", Options = Entries.Select(item => item.Id).ToList(), DefaultValue = ContentJson.Value(id) },
                Field("backgroundColor", "Background", "color", info.Background), Field("accentColor", "Accent", "color", info.Accent),
                Field("textColor", "Text color", "color", info.Ink), Field("span", "Layout width", "select", "full")
            ]
        });
        template.Blocks[^1].Fields.Single(field => field.Id == "span").Options = ["full", "half", "third", "two-thirds"];
        template.Regions.Single(region => region.Id == "main").AllowedBlocks.Add("work-landing");
        template.DefaultBlocks = [new() { Id = "work-entrance", Type = "work-landing", Region = "main", Values = [] }];
        TemplateValidator.ValidateDefinition(template);
        return template;
    }

    private static BaseTemplateInfo Info(string id, string title, string description, string background, string accent, string ink)
        => new() { Id = id, Title = title, Description = description, Category = Category, DemoTitle = "A world of ideas", Background = background, Accent = accent, Ink = ink };
    private static FieldDefinition Field(string id, string title, string type, string value)
        => new() { Id = id, Title = title, Type = type, DefaultValue = ContentJson.Value(value) };
    private static string Eyebrow(string id) => id switch
    {
        "work-landing-course" => "LEARN / EXPLORE / PUT IT INTO PRACTICE", "work-landing-gallery" => "AN EXHIBITION OF IDEAS",
        "work-landing-journal" => "A JOURNAL / AN OPEN INVITATION", "work-landing-handbook" => "THE HANDBOOK / START HERE",
        "work-landing-directory" => "ONE WORK / MANY CONNECTIONS", "work-landing-minimal" => "A WORK TO READ", _ => "A COLLECTION WORTH EXPLORING"
    };
    private static string Introduction(string id) => id switch
    {
        "work-landing-course" => "Follow the lessons in order, or return to the ideas you want to practice. Every Page in this Work is collected below.",
        "work-landing-handbook" => "A clear place to begin. Find the topic you need, follow a useful thread, and keep this handbook close.",
        "work-landing-gallery" => "A collection of studies, perspectives, and discoveries. Choose a Page below and spend a little time with what catches your eye.",
        "work-landing-journal" => "Thoughts gathered with care, ready to be read in your own time. Begin at the beginning or follow a curiosity.",
        "work-landing-minimal" => "Read from the beginning, or choose a Page from the contents below.",
        "atlas-work-landing-observatory" => "Explore a connected body of ideas. Follow the reading path or chart your own course through the Pages below.",
        "atlas-work-landing-atlas" => "A map of the ideas inside this Work. Find a starting point, trace a connection, and discover what lies beyond it.",
        "atlas-work-landing-archive" => "An open archive for considered reading. Each Page contributes a different view of the larger story.",
        _ => "Welcome to a connected collection of ideas. Start with the first Page, or choose the part of the story you want to explore."
    };
}
