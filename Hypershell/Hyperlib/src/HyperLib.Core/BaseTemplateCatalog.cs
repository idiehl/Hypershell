using System.Text.Json;

namespace HyperLib.Core;

/// <summary>Versioned starter designs. Every returned schema, page and appearance is an independent copy.</summary>
public static class BaseTemplateCatalog
{
    public const string DefaultStarterId = "woodland-editorial";
    private static readonly BaseTemplateInfo[] Entries =
    [
        Info("woodland-editorial", "Woodland Editorial", "The original warm field notebook, with a long-form opening, survey, and image gallery.", "Originals", "A Woodland Habitat", "#f5f3ed", "#376954", "#202b26", true),
        Info("wave-laboratory", "Wave Laboratory", "The original dark science workspace, with a wave experiment and playable media.", "Originals", "Exploring a Wave", "#101a21", "#7fa7ee", "#e9f1fc", true),
        Info("portfolio-grid", "Portfolio Grid", "A typographic introduction, paired project cards, a three-column capabilities row, and contact form.", "Creative", "Avery / Selected Work", "#f1f0ec", "#e14c38", "#20211f"),
        Info("field-journal", "Field Journal", "A literary publication with an opening spread, narrow margin notes, and a reading-column rhythm.", "Publication", "The Shape of a Quiet Morning", "#f5eddb", "#8c4732", "#392f27"),
        Info("architecture-studio", "Architecture Studio", "Oversized project photography, a two-thirds narrative, and restrained numbered specification panels.", "Design", "House Between the Pines", "#e9e7e0", "#75624b", "#272725"),
        Info("course-notebook", "Course Notebook", "A lesson opener followed by objectives, a guided exercise, a checklist, and a reflection form.", "Learning", "Seeing in Systems", "#f6f3ff", "#6950a3", "#302747"),
        Info("recipe-kitchen", "Recipe Kitchen", "A recipe card with a wide hero, ingredient sidebar, method column, and useful kitchen timer.", "Food", "Roasted Harvest Bowl", "#fff8e9", "#af5636", "#493023"),
        Info("event-poster", "Event Poster", "A centered oversized poster, three schedule columns, venue map, and registration preview.", "Events", "Night School / Make Together", "#17122d", "#f2b84b", "#fff0d2"),
        Info("travel-atlas", "Travel Atlas", "A destination feature, interactive route checklist, location map, and practical side notes.", "Travel", "A Slow Weekend in Portland", "#edf4f1", "#20758a", "#203c42"),
        Info("product-launch", "Product Launch", "A product-led hero, three benefits, a demo area, comparison note, and clear call to action.", "Product", "Meet Orbit Desk", "#101824", "#a6c4ff", "#f1f5ff"),
        Info("podcast-studio", "Podcast Studio", "Cover art beside episode notes, a large audio player, transcript, and listening recommendations.", "Audio", "Small Signals / Episode 07", "#241827", "#f1b6cb", "#fff0f5"),
        Info("gallery-wall", "Gallery Wall", "A quiet exhibition label, full-width image pair, offset captions, and a visitor note.", "Art", "Studies in Light and Form", "#f8f7f3", "#736457", "#302c28"),
        Info("documentation-desk", "Documentation Desk", "A monospace guide with overview, quick-start steps, reference sidebars, and an interactive checklist.", "Reference", "Build a Useful Component", "#eef1f4", "#36617c", "#263540"),
        Info("nonprofit-story", "Nonprofit Story", "A people-first banner, impact cards, project story, local map, and volunteer form.", "Community", "A Garden for Everyone", "#f6f4df", "#537541", "#303d2b"),
        Info("wellness-retreat", "Wellness Retreat", "An airy centered introduction, gentle image spread, reflection, and quiet guided timer.", "Wellbeing", "A Little Room to Breathe", "#f0e9e6", "#99746b", "#53423e"),
        Info("maker-workbench", "Maker Workbench", "A practical project sheet with materials, build steps, calculator, and tool checklist.", "Making", "Build a Small Desk Shelf", "#292722", "#efb858", "#f4eddb"),
        Info("insight-dashboard", "Insight Dashboard", "A compact operational workspace with three metric cards, comparison panels, and a planning checklist.", "Dashboard", "The Week at a Glance", "#101a22", "#58bac2", "#edf6f7")
    ];

    public static IReadOnlyList<string> Ids { get; } = Array.AsReadOnly(Entries.Select(item => item.Id).ToArray());
    public static IReadOnlyList<BaseTemplateInfo> Catalog => List();
    public static IReadOnlyList<BaseTemplateInfo> List() => Array.AsReadOnly(ContentJson.Clone(Entries));
    public static bool Contains(string? id) => id is not null && Entries.Any(item => item.Id == id);

    public static TemplateDefinition CreateTemplate(string id)
    {
        var info = Find(id);
        var template = StarterTemplate.Create();
        template.Title = info.Title;
        var (font, heading, surface, muted) = Style(id);
        var appearance = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["variant"] = id, ["background"] = info.Background, ["accent"] = info.Accent, ["ink"] = info.Ink,
            ["surface"] = surface, ["muted"] = muted, ["font"] = font, ["headingFont"] = heading
        };
        foreach (var field in template.Appearance)
            if (appearance.TryGetValue(field.Id, out var value)) field.DefaultValue = ContentJson.Value(value);
        template.DefaultBlocks = BuildBlocks(id);
        TemplateValidator.ValidateDefinition(template);
        return template;
    }

    public static Dictionary<string, JsonElement> CreateAppearance(string id)
        => CreateTemplate(id).Appearance.Where(field => field.DefaultValue is not null)
            .ToDictionary(field => field.Id, field => field.DefaultValue!.Value.Clone(), StringComparer.Ordinal);

    public static PageDocument CreateDemoPage(string starterId, string pageId, string workId, string? title = null)
    {
        TemplateValidator.ValidateId(pageId, "page");
        TemplateValidator.ValidateId(workId, "work");
        title ??= Find(starterId).DemoTitle;
        ContentRules.ValidateTitle(title);
        var page = TemplateValidator.CreateDefaultPage(pageId, workId, title, CreateTemplate(starterId));
        if (starterId == "wave-laboratory") page.Sources = ContentJson.Clone(SeedLibrary.Create().Pages["exploring-wave"].Sources);
        return page;
    }

    private static BaseTemplateInfo Find(string? id) => Entries.FirstOrDefault(item => item.Id == id)
        ?? throw new ContentValidationException($"Unknown base template '{id}'. Choose a listed starter.");

    private static BaseTemplateInfo Info(string id, string title, string description, string category, string demoTitle,
        string background, string accent, string ink, bool original = false)
        => new() { Id = id, Title = title, Description = description, Category = category, DemoTitle = demoTitle,
            Background = background, Accent = accent, Ink = ink, IsOriginal = original };

    private static (string Font, string Heading, string Surface, string Muted) Style(string id) => id switch
    {
        "woodland-editorial" => ("humanist", "serif", "#fffdf6", "#647069"),
        "wave-laboratory" => ("system", "monospace", "#192b38", "#a9bdcf"),
        "portfolio-grid" => ("system", "geometric", "#ffffff", "#72716c"),
        "field-journal" => ("serif", "serif", "#fffcf1", "#82705f"),
        "architecture-studio" => ("system", "system", "#f7f6f1", "#76756f"),
        "course-notebook" => ("humanist", "geometric", "#ffffff", "#7b718e"),
        "recipe-kitchen" => ("humanist", "serif", "#fffdf6", "#8a705e"),
        "event-poster" => ("system", "geometric", "#2b2144", "#c4aed1"),
        "travel-atlas" => ("humanist", "serif", "#ffffff", "#658080"),
        "product-launch" => ("system", "geometric", "#192638", "#9bb0ca"),
        "podcast-studio" => ("system", "serif", "#37273d", "#c7a9c7"),
        "gallery-wall" => ("system", "serif", "#ffffff", "#8a8078"),
        "documentation-desk" => ("system", "monospace", "#ffffff", "#6c7b85"),
        "nonprofit-story" => ("humanist", "geometric", "#fffdf1", "#788368"),
        "wellness-retreat" => ("humanist", "serif", "#faf6f2", "#97817b"),
        "maker-workbench" => ("monospace", "monospace", "#3c3930", "#bcb49d"),
        "insight-dashboard" => ("system", "system", "#1a2a34", "#94adb6"),
        _ => throw new ContentValidationException("Unknown template design.")
    };

    private static List<PageBlock> BuildBlocks(string id)
    {
        if (id is "woodland-editorial" or "wave-laboratory")
        {
            var original = SeedLibrary.Create().Pages[id == "woodland-editorial" ? "woodland-habitat" : "exploring-wave"];
            return [Header("page-header", id == "woodland-editorial" ? "NATURAL WORLD / FIELD NOTES" : "SCIENCE LAB / EXPERIMENT", original.Title,
                    id == "woodland-editorial" ? "Notice the layers of a living landscape." : "A small experiment in patterns, motion, and sound."),
                .. ContentJson.Clone(original.Blocks), Divider("page-divider", ""),
                Footer("page-footer", id == "woodland-editorial" ? "Woodland Editorial / Keep exploring" : "Wave Laboratory / Observe, adjust, compare")];
        }
        var image = $"/media/base-templates/{id}.svg";
        return id switch
        {
            "portfolio-grid" =>
            [
                Header("intro", "DESIGN / DEVELOPMENT / IDEAS", "Avery makes useful things.", "Selected experiments in digital products, objects, and everyday systems."),
                Banner("project-one", "01 / DIGITAL", "A clearer daily rhythm", "A compact workspace that helps a small team plan together.", image, "half"),
                Banner("project-two", "02 / PHYSICAL", "Objects with a purpose", "A series of simple forms made to be touched, moved, and used.", image, "half"),
                Divider("capabilities", "WHAT I BRING"),
                Callout("research", "01 / Notice", "Observe the real problem before drawing a solution.", "third"),
                Callout("design", "02 / Shape", "Turn a useful insight into a legible experience.", "third"),
                Callout("build", "03 / Make", "Prototype, test, and keep the useful parts.", "third"),
                Form("contact", "Have something in mind?", "Describe a project in this local form preview."),
                Footer("closing", "Selected work / An independent practice")
            ],
            "field-journal" =>
            [
                Header("masthead", "FIELD NOTES / VOLUME 01", "The shape of a quiet morning", "An illustrated journal about paying attention."),
                Image("opening-image", image, "An abstract landscape study", "Light changes the familiar into something worth seeing again."),
                Rich("essay", "## Begin with what is nearby\n\nA room has its own weather. Light collects on the edge of a table; a small sound briefly interrupts the ordinary. Good notes start here, with something concrete.\n\nTry naming what you can see before deciding what it means.", "two-thirds"),
                Callout("margin-note", "In the margin", "Observe first. Interpret second. Leave room for uncertainty.", "third"),
                Rich("second-column", "## A practice of noticing\n\n- Return to one place.\n- Record one precise detail.\n- Compare it with yesterday.\n\nA repeated observation can become a story.", "half"),
                Rich("reading-column", "## A question to carry\n\nWhat changed while you were looking elsewhere? Keep that question open as you walk home.", "half"),
                Divider("end-mark", "END NOTE", "dots"),
                Footer("colophon", "A small publication / Written slowly / Issue 01")
            ],
            "architecture-studio" =>
            [
                Header("project-header", "STUDIO NORTH / RESIDENTIAL", "House between the pines", "A study in light, shelter, and the space between rooms."),
                Image("project-hero", image, "Architectural massing and material study", "A conceptual project study, shown as an abstract illustration."),
                Rich("project-story", "## Built around a clearing\n\nThe plan gathers shared spaces around a protected outdoor room. A long roofline connects them while allowing the landscape to remain visible.\n\nMaterials are restrained: timber, pale mineral surfaces, and a dark protective edge.", "two-thirds"),
                Callout("project-facts", "Project notes", "Concept study\nResidential / Small footprint\nLight / Threshold / Landscape", "third"),
                Divider("specifications", "DETAILS / 01–03"),
                Callout("light", "01 / Light", "Openings frame one useful view at a time.", "third"),
                Callout("material", "02 / Material", "A short palette supports a coherent whole.", "third"),
                Callout("threshold", "03 / Threshold", "The transition is part of the room.", "third"),
                Footer("project-footer", "Studio North / Concept portfolio")
            ],
            "course-notebook" =>
            [
                Header("lesson-header", "LESSON 01 / SYSTEMS THINKING", "Seeing in systems", "Notice connections, test assumptions, and make a small model."),
                Callout("learning-goal", "What you will practice", "Identify a boundary, trace one feedback loop, and explain a useful change.", "two-thirds"),
                B("duration", "counter", ("label", "Minutes explored"), ("start", 0), ("span", "third")),
                Rich("lesson", "## Start with a familiar system\n\nChoose a garden, a kitchen, or a shared calendar. List the parts and draw connections between them.\n\n**A connection matters when changing one part changes another.**"),
                Tool("exercise", "Your first model", "checklist", 0, "Name the purpose\nDraw a boundary\nConnect three parts\nFind one feedback loop", "half"),
                Callout("worked-example", "Try the garden", "Sunlight supports growth. Growth changes shade. Shade changes what can grow next.", "half"),
                Divider("reflection-heading", "REFLECT"),
                Form("reflection", "Explain it in your own words", "Use the message field as a local reflection preview. Nothing is submitted."),
                Footer("lesson-footer", "Course notebook / Continue when you can explain one connection")
            ],
            "recipe-kitchen" =>
            [
                Header("recipe-header", "THE EVERYDAY TABLE", "Roasted harvest bowl", "A flexible template for a warm, colorful meal."),
                Image("recipe-photo", image, "Illustrated seasonal ingredients", "Swap ingredients for what is in season and what you enjoy."),
                Rich("ingredients", "## Ingredients\n\n- 2 cups chopped seasonal vegetables\n- 1 cup cooked grain\n- 1 cup cooked beans\n- Olive oil and seasoning\n- A spoonful of your favorite dressing", "third"),
                Rich("method", "## Method\n\n1. Cut the vegetables into similar-sized pieces.\n2. Roast until tender, checking as they cook.\n3. Warm the grains and beans.\n4. Arrange in a bowl and add dressing.\n\nAdjust the quantities and seasoning to suit your table.", "two-thirds"),
                Tool("kitchen-timer", "Ten-minute check-in", "timer", 600, "", "half"),
                Callout("cook-note", "Make it yours", "Add something crisp, something bright, and something creamy. Use the timer as a reminder to check progress.", "half"),
                Divider("serving", "AT THE TABLE", "dots"),
                Footer("recipe-footer", "A demonstration recipe layout / Edit the ingredients and instructions for your own recipe")
            ],
            "event-poster" =>
            [
                B("poster", "banner", ("eyebrow", "ONE EVENING / MANY POSSIBILITIES"), ("title", "NIGHT SCHOOL"), ("text", "Make together. Share what you know. Leave with a first attempt."), ("src", image), ("alt", "Abstract evening workshop poster"), ("align", "center")),
                Divider("schedule-heading", "THE EVENING"),
                Callout("doors", "18:00 / Arrive", "Meet the room, collect materials, and find a place to begin.", "third"),
                Callout("workshop", "18:30 / Make", "A guided introduction followed by time to experiment.", "third"),
                Callout("share", "20:00 / Share", "A short, friendly show-and-tell. Unfinished work is welcome.", "third"),
                Map("venue", "Find the workshop", 45.52, -122.68, "Example venue area. Replace with the real event coordinates.", "half"),
                Callout("access", "Before you come", "Bring curiosity. Materials are provided. Add your accessibility and arrival information here.", "half"),
                Form("registration", "Save a place", "Registration form demonstration; no booking or submission occurs."),
                Footer("event-footer", "Night School / An example event page")
            ],
            "travel-atlas" =>
            [
                Banner("destination", "THE SLOW ROUTE / CITY 01", "A weekend without rushing", "A small itinerary built around walking, resting, and one good view.", image),
                Map("city-map", "Portland / Start here", 45.52, -122.68, "A location reference, not a live itinerary or opening-hours guide.", "two-thirds"),
                Callout("pocket-guide", "In your pocket", "Comfortable shoes\nA reusable bottle\nA flexible plan\nRoom for a detour", "third"),
                Rich("morning", "## Morning / Walk and notice\n\nBegin near the river. Let a short walk introduce the city before choosing the next stop.", "half"),
                Rich("afternoon", "## Afternoon / Make room\n\nChoose one neighborhood and give it time. A park bench can be part of the itinerary.", "half"),
                Tool("route", "Your unhurried route", "checklist", 0, "Take a riverside walk\nFind a neighborhood café\nVisit a green space\nWrite one memory"),
                Footer("travel-footer", "Illustrative travel notebook / Check current access and opening details before visiting")
            ],
            "product-launch" =>
            [
                B("product-hero", "banner", ("eyebrow", "A SMALLER WORKSPACE FOR BIG IDEAS"), ("title", "Meet Orbit Desk."), ("text", "Keep the important pieces close. Give everything else some space."), ("src", image), ("alt", "Abstract product workspace illustration"), ("align", "center")),
                Callout("benefit-focus", "One clear view", "Bring the next useful action into focus.", "third"),
                Callout("benefit-flex", "Room to adapt", "Arrange the workspace around the way you think.", "third"),
                Callout("benefit-local", "A place of your own", "Keep your project notes connected to the work.", "third"),
                Divider("demo-heading", "SEE THE IDEA"),
                B("product-demo", "embed", ("url", ""), ("title", "Add a product walkthrough"), ("caption", "Set a supported external video URL to show a demo. External content requires a click."), ("span", "two-thirds")),
                Callout("product-details", "Designed to stay simple", "A focused starting point. Replace this example copy with concrete features and a clear audience.", "third"),
                Form("interest", "Stay in the loop", "Interest form preview; no email is sent or saved by this demonstration."),
                Footer("product-footer", "Orbit Desk / Example product concept")
            ],
            "podcast-studio" =>
            [
                Image("episode-art", image, "Small Signals episode artwork", "EPISODE 07", "third"),
                B("episode-title", "header", ("eyebrow", "SMALL SIGNALS / CONVERSATIONS ABOUT MAKING"), ("title", "The value of a first attempt"), ("text", "A thoughtful conversation about prototypes, uncertainty, and learning in public."), ("span", "two-thirds")),
                B("episode-audio", "audio", ("src", "/media/sample-tone.wav"), ("caption", "Audio-player demonstration using a short reference tone, not a recorded episode.")),
                Rich("show-notes", "## In this episode\n\n- Why an unfinished model can start a useful conversation.\n- How constraints make the next step clearer.\n- What to keep when an experiment changes direction.", "two-thirds"),
                Callout("episode-credit", "At the microphone", "Host / Add a name\nGuest / Add a name\nRecorded / Add a date", "third"),
                Divider("transcript-label", "TRANSCRIPT PREVIEW"),
                Rich("transcript", "**Host:** What makes a first attempt useful?\n\n**Guest:** It gives us something concrete to learn from. We can stop imagining every possibility and notice what actually happens."),
                Footer("podcast-footer", "Small Signals / A podcast page demonstration")
            ],
            "gallery-wall" =>
            [
                B("exhibition-label", "header", ("eyebrow", "STUDIO EXHIBITION / 01"), ("title", "Studies in light and form"), ("text", "A small collection of arrangements, pauses, and changing perspectives."), ("align", "center")),
                B("pair", "gallery", ("src1", image), ("src2", image), ("caption", "Two positions within the same visual study.")),
                Callout("work-label", "Study 01", "Digital composition\nShape, balance, and negative space", "third"),
                Rich("artist-note", "## A note from the studio\n\nThese studies begin with a small number of forms. Their relationships matter more than their individual complexity.\n\nMove closer, then step back. Notice what changes.", "two-thirds"),
                Divider("pause", "", "space"),
                Image("final-study", image, "A final composition study", "Study 02 / A different way of holding the same space."),
                Footer("gallery-footer", "An example exhibition / Images are editable template illustrations")
            ],
            "documentation-desk" =>
            [
                Header("guide-title", "REFERENCE / START HERE", "Build a useful component", "A short guide from a clear contract to a working example."),
                Callout("overview", "The contract", "Inputs are explicit. Outputs are predictable. Content stays separate from presentation.", "two-thirds"),
                Callout("prerequisites", "Before you begin", "A project\nA small use case\nA way to test the result", "third"),
                Rich("quick-start", "## Quick start\n\n1. Name the thing the component does.\n2. Define the fields it needs.\n3. Render a useful empty state.\n4. Try representative content.\n5. Keep the contract stable.", "two-thirds"),
                Tool("review", "Review checklist", "checklist", 0, "Fields have clear names\nEmpty values render safely\nKeyboard use works\nContent survives an edit", "third"),
                Divider("reference-label", "FIELD REFERENCE"),
                Rich("reference", "## Example fields\n\n**title** — A short, meaningful heading.\n\n**text** — The body content in plain text or supported Markdown.\n\n**url** — An optional safe link.\n\nKeep examples close to the rules they explain."),
                Footer("guide-footer", "Documentation Desk / Replace this guide with your own project reference")
            ],
            "nonprofit-story" =>
            [
                Banner("story-hero", "GROWING A COMMON PLACE", "A garden for everyone", "A neighborhood project built one shared afternoon at a time.", image),
                Callout("impact-one", "A place to meet", "Create a welcoming space for everyday connection.", "third"),
                Callout("impact-two", "A chance to learn", "Share practical skills across ages and experience.", "third"),
                Callout("impact-three", "Something to care for", "Make a small, visible contribution together.", "third"),
                Rich("story", "## Small actions, shared ownership\n\nThe garden starts with listening: what does the neighborhood need, and who wants to help?\n\nEach session leaves room for learning, conversation, and a task that can be completed together.", "two-thirds"),
                Callout("invitation", "You can take part", "Bring an idea, lend a hand, or simply come and meet the group.", "third"),
                Map("project-map", "The neighborhood", 45.52, -122.68, "Replace these example coordinates with your project location."),
                Form("volunteer", "Join a work day", "Volunteer form demonstration; this preview does not contact an organization."),
                Footer("community-footer", "An illustrative community project / Replace example claims with verified details")
            ],
            "wellness-retreat" =>
            [
                B("welcome", "header", ("eyebrow", "A PAUSE IN THE DAY"), ("title", "A little room to breathe"), ("text", "A quiet page for noticing, reflecting, and taking an unhurried break."), ("align", "center")),
                Image("quiet-image", image, "Soft abstract landscape", "Find a comfortable place to pause."),
                Rich("reflection", "## Arrive where you are\n\nNotice the support beneath you, the light around you, and one sound in the room. There is nothing to complete.\n\nUse this space as a personal reflection, at your own pace.", "half"),
                Tool("pause-timer", "A two-minute pause", "timer", 120, "", "half"),
                Divider("breathing-room", "", "space"),
                B("gentle-note", "callout", ("title", "A thought to keep"), ("text", "A small pause can be enough. Return to the day when you are ready."), ("align", "center")),
                Footer("quiet-footer", "A reflective layout / Adapt the words and timing to your own practice")
            ],
            "maker-workbench" =>
            [
                Header("build-header", "WORKBENCH / PROJECT 01", "Build a small desk shelf", "A project sheet for planning, measuring, and learning through a first build."),
                Image("build-plan", image, "A conceptual shelf and material diagram", "Illustrative plan only; adapt dimensions and methods to your materials."),
                Rich("materials", "## Materials to plan\n\n- Shelf board\n- Two supports\n- Suitable fixings\n- A finish appropriate to the material\n\nCheck dimensions, stability, and tool instructions before building.", "third"),
                Rich("build-steps", "## Build sequence\n\n1. Sketch the intended size and purpose.\n2. Check the space where it will sit.\n3. Mark dimensions before cutting.\n4. Test the fit before final assembly.\n5. Inspect the finished piece before use.", "two-thirds"),
                Tool("measurements", "A quick calculation", "calculator", 0, "", "half"),
                Tool("build-checklist", "Before the first cut", "checklist", 0, "Check material dimensions\nRead tool instructions\nPrepare a clear work area\nMark the cut lines", "half"),
                Callout("build-log", "Keep a build log", "Record one measurement that changed and one detail you would improve next time."),
                Footer("workbench-footer", "Maker Workbench / A planning template, not a certified construction plan")
            ],
            "insight-dashboard" =>
            [
                Header("dashboard-header", "WORKSPACE / WEEKLY REVIEW", "The week at a glance", "Illustrative numbers and a practical place to choose the next step."),
                B("metric-projects", "counter", ("label", "Active projects"), ("start", 4), ("span", "third")),
                B("metric-finished", "counter", ("label", "Tasks finished"), ("start", 12), ("span", "third")),
                B("metric-experiments", "counter", ("label", "Experiments running"), ("start", 3), ("span", "third")),
                Callout("progress", "Moving forward", "One useful improvement shipped. Two assumptions tested. A clearer next step for the oldest project.", "half"),
                Callout("attention", "Needs attention", "Choose one stalled task and make it smaller. Review any commitment that no longer serves the goal.", "half"),
                Divider("planning-heading", "PLAN THE NEXT CYCLE"),
                Tool("weekly-checklist", "Review and reset", "checklist", 0, "Review completed work\nChoose the next priority\nClear one blocker\nLeave space for the unexpected", "two-thirds"),
                Tool("focus", "A short focus block", "timer", 900, "", "third"),
                Footer("dashboard-footer", "Demo workspace / Counters are illustrative and are not connected to analytics")
            ],
            _ => throw new ContentValidationException("Unknown template design.")
        };
    }

    private static PageBlock B(string id, string type, params (string Key, object Value)[] values)
        => new() { Id = id, Type = type, Region = "main", Values = values.ToDictionary(item => item.Key, item => ContentJson.Value(item.Value), StringComparer.Ordinal) };
    private static PageBlock Header(string id, string eyebrow, string title, string text)
        => B(id, "header", ("eyebrow", eyebrow), ("title", title), ("text", text));
    private static PageBlock Footer(string id, string text) => B(id, "footer", ("text", text));
    private static PageBlock Divider(string id, string label, string style = "line") => B(id, "divider", ("label", label), ("style", style));
    private static PageBlock Rich(string id, string text, string span = "full") => B(id, "richtext", ("text", text), ("span", span));
    private static PageBlock Callout(string id, string title, string text, string span = "full") => B(id, "callout", ("title", title), ("text", text), ("span", span));
    private static PageBlock Image(string id, string src, string alt, string caption, string span = "full") => B(id, "image", ("src", src), ("alt", alt), ("caption", caption), ("width", 100), ("span", span));
    private static PageBlock Banner(string id, string eyebrow, string title, string text, string src, string span = "full") => B(id, "banner", ("eyebrow", eyebrow), ("title", title), ("text", text), ("src", src), ("alt", title), ("span", span));
    private static PageBlock Form(string id, string title, string description) => B(id, "form", ("title", title), ("description", description));
    private static PageBlock Map(string id, string title, double latitude, double longitude, string caption, string span = "full") => B(id, "map", ("title", title), ("latitude", latitude), ("longitude", longitude), ("zoom", 12), ("caption", caption), ("span", span));
    private static PageBlock Tool(string id, string title, string mode, int initial, string items, string span = "full") => B(id, "tool", ("title", title), ("mode", mode), ("initial", initial), ("items", items), ("span", span));
}
