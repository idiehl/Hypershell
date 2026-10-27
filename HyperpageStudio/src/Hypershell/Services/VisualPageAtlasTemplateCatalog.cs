using Hypershell.Models;

namespace Hypershell.Services;

internal static class VisualPageAtlasTemplateCatalog
{
    public const string LibraryName = "Atlas Archive";

    public static IReadOnlyDictionary<string, string> ThemeTokens { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["color.background"] = "#070d1a",
            ["color.surface"] = "#101b35",
            ["color.surface-deep"] = "#0b1226",
            ["color.text"] = "#e8ecf1",
            ["color.heading"] = "#edf3ff",
            ["color.muted"] = "#8a94a6",
            ["color.silver"] = "#bcc5d0",
            ["color.accent"] = "#c8963e",
            ["color.pale-gold"] = "#e8c97a",
            ["color.border-subtle"] = "rgba(138, 159, 193, 0.24)",
            ["font.sans"] = "Montserrat, 'Segoe UI', 'Helvetica Neue', Arial, sans-serif",
            ["font.mono"] = "'JetBrains Mono', Consolas, 'SFMono-Regular', Menlo, monospace",
            ["font.display"] = "'Playfair Display', Georgia, serif",
            ["radius.card"] = "14.4px",
            ["shadow.soft"] = "0 14px 36px rgba(0, 0, 0, 0.28)",
            ["space.section"] = "clamp(3rem, 8vw, 7rem)"
        };

    public static IReadOnlyList<VisualPageTemplateDocument> CreateTemplates() =>
    [
        CreateLongformPage(),
        CreateHistoryPage(),
        CreateLanguageCulturePage(),
        CreateEvolutionPage(),
        CreateCodingPage(),
        CreateMediaDossierPage(),
        SectionTemplate(
            "atlas.archive.section.lead.v1",
            "Archive article introduction",
            "Editorial sections",
            "Kicker, title, summary, reading metadata, and on-page navigation.",
            "Use at the beginning of an informational article, below the site shell.",
            ArchiveIntroduction()),
        SectionTemplate(
            "atlas.archive.section.chapter.v1",
            "Archive long-form chapter",
            "Editorial sections",
            "A readable long-form chapter with a heading, paragraphs, callout, and figure.",
            "Duplicate this section for each major chapter in a long article.",
            LongformChapter()),
        SectionTemplate(
            "atlas.archive.section.timeline.v1",
            "Atlas historical timeline",
            "History",
            "Chronological milestones with dates, headings, and contextual notes.",
            "Use for historical periods, biographies, releases, or evolutionary stages.",
            TimelineSection()),
        SectionTemplate(
            "atlas.archive.section.gallery.v1",
            "Atlas image gallery",
            "Media & data",
            "Responsive three-image editorial gallery with accessible captions.",
            "Replace each placeholder image and keep every caption and alt description specific.",
            ImageGallery()),
        SectionTemplate(
            "atlas.archive.section.audio.v1",
            "Atlas audio with transcript",
            "Media & data",
            "Audio player, caption, contextual note, and expandable transcript.",
            "Import an audio asset, then preserve the transcript for accessibility and search.",
            AudioTranscript()),
        SectionTemplate(
            "atlas.archive.section.video.v1",
            "Atlas video feature",
            "Media & data",
            "Responsive video, caption, summary, and transcript placeholder.",
            "Add captions or a transcript before publication.",
            VideoFeature()),
        SectionTemplate(
            "atlas.archive.section.data.v1",
            "Atlas graph and data table",
            "Media & data",
            "Chart canvas paired with an accessible source table and interpretation notes.",
            "Keep the table even after wiring the graph so the underlying values remain available.",
            DataEvidenceSection()),
        SectionTemplate(
            "atlas.archive.section.sources.v1",
            "Atlas sources and further reading",
            "References",
            "Numbered references, source notes, and a related-reading card.",
            "Use stable links and describe why each source matters.",
            SourcesSection()),
        ElementTemplate(
            "atlas.archive.element.heading.v1",
            "Atlas kicker and heading",
            "Typography",
            "Small letter-spaced kicker, uppercase heading, and restrained lead paragraph.",
            "Use this heading group to introduce a page or major section.",
            HeadingGroup("Archive topic", "A precise, memorable heading", "Summarize the subject and give the reader a reason to continue.")),
        ElementTemplate(
            "atlas.archive.element.card.v1",
            "Atlas editorial card",
            "Cards & links",
            "Celestial navy content card with a gold metadata label and link.",
            "Use in related-topic grids, chapter overviews, and Archive indexes.",
            EditorialCard()),
        ElementTemplate(
            "atlas.archive.element.callout.v1",
            "Atlas fact callout",
            "Editorial elements",
            "A bordered aside for definitions, cautions, context, or key facts.",
            "Keep callouts brief enough that they do not interrupt the article's reading rhythm.",
            FactCallout()),
        ElementTemplate(
            "atlas.archive.element.figure.v1",
            "Atlas image and caption",
            "Media & data",
            "Responsive image figure with source-aware caption text.",
            "Replace the placeholder, write useful alt text, and identify the image source.",
            ImageFigure("Figure title", "Explain what the image shows and provide its source.")),
        ElementTemplate(
            "atlas.archive.element.quote.v1",
            "Atlas pull quote",
            "Editorial elements",
            "Gold-accented quotation with attribution and source link.",
            "Use sparingly for primary-source language or a pivotal statement.",
            PullQuote()),
        ElementTemplate(
            "atlas.archive.element.glossary.v1",
            "Atlas glossary entry",
            "Language & culture",
            "Term, pronunciation or metadata, and a clear definition.",
            "Duplicate inside a description list to build a glossary.",
            GlossaryEntry()),
        ElementTemplate(
            "atlas.archive.element.code.v1",
            "Atlas code listing",
            "Coding",
            "Technical label, preformatted code, explanation, and copy-ready filename metadata.",
            "Select the preformatted block to replace the sample code.",
            CodeListing()),
        ElementTemplate(
            "atlas.archive.element.citation.v1",
            "Atlas source citation",
            "References",
            "Numbered source with title, author or organization, date, and link.",
            "Use inside an ordered references list.",
            CitationItem()),
        ElementTemplate(
            "atlas.archive.element.divider.v1",
            "Atlas section divider",
            "Editorial elements",
            "Subtle gold-centered divider for transitions between major ideas.",
            "Use only between top-level chapters to preserve hierarchy.",
            Element(
                "html.hr",
                "Atlas section divider",
                styles: Styles(
                    ("border", "0"),
                    ("border-top", "1px solid rgba(200, 150, 62, 0.46)"),
                    ("margin", "clamp(2.5rem, 7vw, 5rem) auto"),
                    ("width", "min(9rem, 34vw)"))))
    ];

    private static VisualPageTemplateDocument CreateLongformPage() => PageTemplate(
        "atlas.archive.page.longform.v1",
        "Atlas Archive long-form article",
        "General research",
        "A complete, media-ready editorial page for deeply researched informational writing.",
        "Start here when the subject does not need a more specialized structure.",
        "Archive Article",
        "/archive/article-title",
        "Article title | Atlas Archive",
        "A researched Atlas Archive article.",
        PageRoot(
            ArchiveHero(
                "Atlas Archive / Field Guide",
                "A clear title for an expansive subject",
                "Introduce the central question, define the scope, and tell readers what they will understand by the end.",
                "Research · 12 minute read"),
            ContentShell(
                OnThisPage("Introduction", "Key ideas", "Evidence", "Media", "Sources"),
                Element(
                    "atlas.editorial",
                    "Long-form article",
                    children:
                    [
                        ArticleSection(
                            "introduction",
                            "Introduction",
                            "Open with the context a new reader needs. Establish definitions, timeframe, location, and the boundaries of the topic.",
                            "Use a second paragraph to explain why the topic matters and how the available evidence should be interpreted."),
                        PullQuote(),
                        ArticleSection(
                            "key-ideas",
                            "Key ideas",
                            "Develop the first major idea in a sequence of short, readable paragraphs. Prefer descriptive headings over generic labels.",
                            "Connect individual facts to the broader argument, and distinguish established evidence from interpretation."),
                        FactCallout(),
                        ImageFigure(
                            "Primary visual evidence",
                            "Describe the image, map, artifact, or diagram and identify its source."),
                        DataEvidenceSection(),
                        AudioTranscript(),
                        VideoFeature(),
                        SourcesSection()
                    ])),
            RelatedTopics()));

    private static VisualPageTemplateDocument CreateHistoryPage() => PageTemplate(
        "atlas.archive.page.history.v1",
        "History chronicle",
        "History",
        "Historical narrative with chronology, primary-source quotation, visual evidence, data, and references.",
        "Use for events, eras, biographies, institutions, or the development of an idea.",
        "History Chronicle",
        "/archive/history-chronicle",
        "History chronicle | Atlas Archive",
        "A contextual history with chronology and sources.",
        PageRoot(
            ArchiveHero(
                "Atlas Archive / History",
                "The making of a historical world",
                "Frame the era, place, people, and central transformation before moving into the chronology.",
                "History · Chronology · Primary sources"),
            ContentShell(
                OnThisPage("Setting", "Chronology", "Voices", "Evidence", "Legacy", "Sources"),
                Element(
                    "atlas.editorial",
                    "Historical narrative",
                    children:
                    [
                        ArticleSection(
                            "setting",
                            "Setting the scene",
                            "Describe the political, geographic, economic, and cultural conditions that shaped the period.",
                            "Name the groups involved and avoid treating later outcomes as inevitable."),
                        TimelineSection(),
                        PullQuote(
                            "Insert a short primary-source passage that reveals how a participant understood the moment.",
                            "Speaker or document · date"),
                        ImageFigure(
                            "Map, artifact, or archival image",
                            "Identify the creator, approximate date, collection, and what the reader should notice."),
                        DataEvidenceSection("Evidence across the period"),
                        ArticleSection(
                            "legacy",
                            "Legacy and interpretation",
                            "Explain what changed, what endured, and where historians disagree.",
                            "Separate contemporary consequences from later memory and myth."),
                        SourcesSection()
                    ])),
            RelatedTopics()));

    private static VisualPageTemplateDocument CreateLanguageCulturePage() => PageTemplate(
        "atlas.archive.page.language-culture.v1",
        "Language and culture field guide",
        "Language & culture",
        "A field guide for language, recorded audio, oral tradition, cultural practice, and meaning in context.",
        "Use when pronunciation, recorded speech, vocabulary, and cultural interpretation are central.",
        "Language and Culture Guide",
        "/archive/language-culture-guide",
        "Language and culture guide | Atlas Archive",
        "A language and culture field guide with audio and glossary material.",
        PageRoot(
            ArchiveHero(
                "Atlas Archive / Language & Culture",
                "Language as a map of lived experience",
                "Introduce the community, linguistic variety, setting, and the limits of translation or outside interpretation.",
                "Language · Culture · Recorded examples"),
            ContentShell(
                OnThisPage("Context", "Listen", "Glossary", "Practice", "Media", "Sources"),
                Element(
                    "atlas.editorial",
                    "Language and culture guide",
                    children:
                    [
                        ArticleSection(
                            "context",
                            "Community and context",
                            "Locate the language or practice within a community, region, and history. Note alternate names and varieties without flattening meaningful differences.",
                            "Explain who uses it, in which settings, and how social context changes meaning."),
                        AudioTranscript(),
                        GlossarySection(),
                        FactCallout(
                            "Translation note",
                            "Explain a word, gesture, or practice whose meaning cannot be carried by a one-to-one translation."),
                        ImageGallery(),
                        VideoFeature(),
                        SourcesSection()
                    ])),
            RelatedTopics()));

    private static VisualPageTemplateDocument CreateEvolutionPage() => PageTemplate(
        "atlas.archive.page.evolution.v1",
        "Evolution and science explainer",
        "Evolution & science",
        "Evidence-led scientific explainer with mechanisms, stages, graph, table, and media.",
        "Use for biological evolution, scientific processes, comparative anatomy, or evidence synthesis.",
        "Evolution Explainer",
        "/archive/evolution-explainer",
        "Evolution explainer | Atlas Archive",
        "An evidence-led scientific explainer.",
        PageRoot(
            ArchiveHero(
                "Atlas Archive / Evolution",
                "How change accumulates across generations",
                "State the scientific question, scale, and evidence before introducing the mechanisms that explain the observed pattern.",
                "Evolution · Evidence · Comparative data"),
            ContentShell(
                OnThisPage("Question", "Mechanisms", "Evidence", "Pattern", "Limits", "Sources"),
                Element(
                    "atlas.editorial",
                    "Scientific explainer",
                    children:
                    [
                        ArticleSection(
                            "question",
                            "The question",
                            "Define the trait, lineage, environment, or transition being explained and identify the timescale involved.",
                            "Describe the observations first; reserve causal claims for the evidence section."),
                        MechanismCards(),
                        ImageFigure(
                            "Comparative evidence",
                            "Label the structures, organisms, or stages and explain why the comparison is informative."),
                        DataEvidenceSection("Pattern in the evidence"),
                        ProcessSteps(),
                        FactCallout(
                            "What the evidence does not show",
                            "Record uncertainty, sampling limits, competing explanations, or common misconceptions."),
                        SourcesSection()
                    ])),
            RelatedTopics()));

    private static VisualPageTemplateDocument CreateCodingPage() => PageTemplate(
        "atlas.archive.page.coding.v1",
        "Coding tutorial and technical guide",
        "Coding",
        "Technical article with prerequisites, numbered steps, code, results, API table, troubleshooting, and references.",
        "Use for tutorials, architecture walkthroughs, language features, and reproducible experiments.",
        "Coding Guide",
        "/archive/coding-guide",
        "Coding guide | Atlas Archive",
        "A reproducible coding tutorial with annotated examples.",
        PageRoot(
            ArchiveHero(
                "Atlas Archive / Coding",
                "Build a small system and understand every decision",
                "State the outcome, intended reader, toolchain, and version assumptions before the first command.",
                "C# · .NET 10 · Reproducible guide"),
            ContentShell(
                OnThisPage("Goal", "Prerequisites", "Build", "Explain", "Troubleshoot", "References"),
                Element(
                    "atlas.editorial",
                    "Technical guide",
                    children:
                    [
                        ArticleSection(
                            "goal",
                            "What you will build",
                            "Describe the finished behavior in concrete terms and show how the pieces fit together.",
                            "Include the versions used so future readers can distinguish code errors from toolchain drift."),
                        Prerequisites(),
                        TutorialSteps(),
                        CodeListing(),
                        ImageFigure(
                            "Expected result",
                            "Replace this with a screenshot or diagram and describe the successful state."),
                        ApiReferenceTable(),
                        Troubleshooting(),
                        SourcesSection("References and next steps")
                    ])),
            RelatedTopics()));

    private static VisualPageTemplateDocument CreateMediaDossierPage() => PageTemplate(
        "atlas.archive.page.media-dossier.v1",
        "Media-rich research dossier",
        "Media & data",
        "A research page organized around images, audio, video, tables, and graph-ready evidence.",
        "Use when primary media and datasets carry as much explanatory weight as the prose.",
        "Research Dossier",
        "/archive/research-dossier",
        "Research dossier | Atlas Archive",
        "A media-rich Atlas Archive research dossier.",
        PageRoot(
            ArchiveHero(
                "Atlas Archive / Research Dossier",
                "Evidence you can see, hear, and examine",
                "Introduce the collection, provenance, selection method, and the question connecting its different media.",
                "Images · Audio · Video · Data"),
            ContentShell(
                OnThisPage("Overview", "Images", "Audio", "Video", "Data", "Sources"),
                Element(
                    "atlas.editorial",
                    "Research dossier",
                    children:
                    [
                        ArticleSection(
                            "overview",
                            "Collection overview",
                            "Explain where the material came from, how it was selected, and which voices or records may be missing.",
                            "Give each medium a purpose in the argument instead of using it as decoration."),
                        ImageGallery(),
                        AudioTranscript(),
                        VideoFeature(),
                        DataEvidenceSection(),
                        SourcesSection()
                    ])),
            RelatedTopics()));

    private static VisualPageTemplateDocument PageTemplate(
        string id,
        string name,
        string category,
        string description,
        string usageHint,
        string suggestedName,
        string suggestedRoute,
        string suggestedTitle,
        string suggestedDescription,
        VisualPageElement root)
        => new()
        {
            TemplateId = id,
            Name = name,
            Description = description,
            Scope = "page",
            StorageScope = "built-in",
            Library = LibraryName,
            Category = category,
            UsageHint = usageHint,
            Tags =
            [
                "archive", "atlas", "editorial", "responsive", "long-form",
                category.ToLowerInvariant()
            ],
            SuggestedPageName = suggestedName,
            SuggestedPageRoute = suggestedRoute,
            SuggestedPageTitle = suggestedTitle,
            SuggestedPageDescription = suggestedDescription,
            ThemeTokens = new Dictionary<string, string>(ThemeTokens, StringComparer.Ordinal),
            Root = root
        };

    private static VisualPageTemplateDocument SectionTemplate(
        string id,
        string name,
        string category,
        string description,
        string usageHint,
        VisualPageElement root)
        => new()
        {
            TemplateId = id,
            Name = name,
            Description = description,
            Scope = "section",
            StorageScope = "built-in",
            Library = LibraryName,
            Category = category,
            UsageHint = usageHint,
            Tags = ["archive", "atlas", "section", category.ToLowerInvariant()],
            Root = root
        };

    private static VisualPageTemplateDocument ElementTemplate(
        string id,
        string name,
        string category,
        string description,
        string usageHint,
        VisualPageElement root)
        => new()
        {
            TemplateId = id,
            Name = name,
            Description = description,
            Scope = "element",
            StorageScope = "built-in",
            Library = LibraryName,
            Category = category,
            UsageHint = usageHint,
            Tags = ["archive", "atlas", "element", category.ToLowerInvariant()],
            Root = root
        };

    private static VisualPageElement PageRoot(params VisualPageElement[] children) => Element(
        "core.page",
        "Atlas Archive page",
        children: children,
        styles: Styles(
            ("background", "radial-gradient(circle at 50% 12%, #111f3d 0%, #0b142d 34%, #070d1a 76%)"),
            ("color", "var(--vp-color-text, #e8ecf1)"),
            ("font-family", "var(--vp-font-sans, Montserrat, 'Segoe UI', sans-serif)"),
            ("line-height", "1.72"),
            ("min-height", "100vh")),
        baseLayout: Layout(display: "flex", direction: "column", gap: "0"));

    private static VisualPageElement ArchiveHero(
        string kicker,
        string title,
        string summary,
        string metadata) => Element(
        "core.section",
        "Archive page introduction",
        children:
        [
            Element(
                "core.paragraph",
                "Topic label",
                kicker,
                styles: KickerStyles()),
            Element(
                "core.heading1",
                "Article title",
                title,
                styles: Styles(
                    ("color", "var(--vp-color-heading, #edf3ff)"),
                    ("font-size", "clamp(2.35rem, 6.2vw, 4.75rem)"),
                    ("font-weight", "300"),
                    ("letter-spacing", "clamp(0.08em, 0.7vw, 0.2em)"),
                    ("line-height", "1.12"),
                    ("margin", "0"),
                    ("max-width", "18ch"),
                    ("text-transform", "uppercase"))),
            Element(
                "core.paragraph",
                "Article summary",
                summary,
                styles: Styles(
                    ("color", "var(--vp-color-silver, #bcc5d0)"),
                    ("font-size", "clamp(1rem, 1.8vw, 1.22rem)"),
                    ("line-height", "1.75"),
                    ("margin", "0"),
                    ("max-width", "68ch"))),
            Element(
                "core.paragraph",
                "Article metadata",
                metadata,
                styles: Styles(
                    ("color", "var(--vp-color-pale-gold, #e8c97a)"),
                    ("font-family", "var(--vp-font-mono, 'JetBrains Mono', monospace)"),
                    ("font-size", "0.78rem"),
                    ("letter-spacing", "0.08em"),
                    ("margin", "0.5rem 0 0")))
        ],
        attributes: Attributes(
            ("aria-label", "Article introduction"),
            ("class", "atlas-archive-hero")),
        styles: Styles(
            ("background", "linear-gradient(160deg, rgba(16, 27, 53, 0.86), rgba(7, 13, 26, 0.42))"),
            ("border-bottom", "1px solid rgba(138, 159, 193, 0.24)")),
        baseLayout: Layout(
            display: "flex",
            direction: "column",
            gap: "1.35rem",
            padding: "clamp(7rem, 14vw, 11rem) clamp(1.25rem, 8vw, 8rem) clamp(4rem, 9vw, 7rem)"));

    private static VisualPageElement ContentShell(
        VisualPageElement navigation,
        VisualPageElement article) => Element(
        "core.container",
        "Archive reading layout",
        children: [navigation, article],
        styles: Styles(
            ("margin", "0 auto"),
            ("width", "min(100%, 82rem)")),
        baseLayout: Layout(
            display: "grid",
            columns: "minmax(0, 1fr)",
            gap: "2.5rem",
            padding: "clamp(3rem, 7vw, 6rem) clamp(1.25rem, 5vw, 4rem)"),
        largeLayout: Layout(
            display: "grid",
            columns: "minmax(13rem, 3fr) minmax(0, 9fr)",
            gap: "clamp(2.5rem, 6vw, 5rem)"));

    private static VisualPageElement OnThisPage(params string[] labels) => Element(
        "core.nav",
        "On this page",
        children:
        [
            Element(
                "core.paragraph",
                "Navigation label",
                "On this page",
                styles: KickerStyles()),
            Element(
                "core.ordered-list",
                "Article links",
                children: labels.Select((label, index) => Element(
                    "core.list-item",
                    $"Section link {index + 1}",
                    children:
                    [
                        Element(
                            "core.link",
                            $"{label} link",
                            label,
                            attributes: Attributes(("href", $"#{Slug(label)}")),
                            styles: Styles(
                                ("color", "var(--vp-color-silver, #bcc5d0)"),
                                ("text-decoration", "none")))
                    ],
                    styles: Styles(("margin", "0.55rem 0")))).ToList(),
                styles: Styles(
                    ("margin", "0"),
                    ("padding-left", "1.25rem")))
        ],
        attributes: Attributes(("aria-label", "On this page")),
        styles: CardStyles(),
        baseLayout: Layout(
            display: "flex",
            direction: "column",
            gap: "0.75rem",
            padding: "1.2rem"));

    private static VisualPageElement ArchiveIntroduction() => Element(
        "core.section",
        "Archive article introduction",
        children:
        [
            HeadingGroup(
                "Atlas Archive / Topic",
                "A compelling article title",
                "Explain the scope and central question in one or two clear sentences."),
            Element(
                "core.paragraph",
                "Reading metadata",
                "Research · Updated date · 10 minute read",
                styles: Styles(
                    ("color", "var(--vp-color-pale-gold, #e8c97a)"),
                    ("font-family", "var(--vp-font-mono, monospace)"),
                    ("font-size", "0.78rem"))),
            OnThisPage("Introduction", "Evidence", "Media", "Sources")
        ],
        attributes: Attributes(("aria-label", "Article introduction")),
        baseLayout: Layout(
            display: "flex",
            direction: "column",
            gap: "1.5rem",
            padding: "clamp(2.5rem, 7vw, 5rem)"));

    private static VisualPageElement HeadingGroup(
        string kicker,
        string heading,
        string summary) => Element(
        "core.container",
        "Atlas heading group",
        children:
        [
            Element("core.paragraph", "Kicker", kicker, styles: KickerStyles()),
            Element(
                "core.heading2",
                "Heading",
                heading,
                styles: HeadingStyles()),
            Element(
                "core.paragraph",
                "Lead paragraph",
                summary,
                styles: LeadStyles())
        ],
        baseLayout: Layout(display: "flex", direction: "column", gap: "0.85rem"));

    private static VisualPageElement LongformChapter() => Element(
        "atlas.editorial",
        "Long-form chapter",
        children:
        [
            ArticleSection(
                "chapter-title",
                "Descriptive chapter heading",
                "Begin with the claim, question, or transition that gives this chapter a clear purpose.",
                "Develop the evidence in short paragraphs. Introduce unfamiliar terms before relying on them."),
            FactCallout(),
            ImageFigure("Supporting figure", "Explain what readers should notice and name the source."),
            Element(
                "core.paragraph",
                "Chapter conclusion",
                "Close by connecting the chapter's evidence to the larger article rather than merely repeating its opening.")
        ],
        baseLayout: Layout(display: "flex", direction: "column", gap: "1.35rem"));

    private static VisualPageElement ArticleSection(
        string id,
        string heading,
        string firstParagraph,
        string secondParagraph) => Element(
        "core.section",
        heading,
        children:
        [
            Element("core.heading2", $"{heading} heading", heading, styles: HeadingStyles()),
            Element("core.paragraph", $"{heading} opening", firstParagraph),
            Element("core.paragraph", $"{heading} detail", secondParagraph)
        ],
        attributes: Attributes(("id", id), ("aria-label", heading)),
        styles: Styles(
            ("max-width", "72ch"),
            ("scroll-margin-top", "6rem")),
        baseLayout: Layout(display: "flex", direction: "column", gap: "1rem"));

    private static VisualPageElement TimelineSection() => Element(
        "core.section",
        "Chronology",
        children:
        [
            HeadingGroup(
                "Chronology",
                "Turning points across time",
                "Use a consistent date system and explain why each event changes the story."),
            Element(
                "core.ordered-list",
                "Timeline events",
                children:
                [
                    TimelineEntry("Date or period", "First turning point", "Describe what changed, who was affected, and what evidence supports the date."),
                    TimelineEntry("Date or period", "Second turning point", "Connect this event to the preceding conditions and avoid implying a single cause."),
                    TimelineEntry("Date or period", "Third turning point", "Explain the immediate outcome and the longer consequences separately.")
                ],
                styles: Styles(
                    ("list-style", "none"),
                    ("margin", "0"),
                    ("padding", "0")),
                baseLayout: Layout(display: "flex", direction: "column", gap: "1rem"))
        ],
        attributes: Attributes(("id", "chronology"), ("aria-label", "Chronology")),
        baseLayout: Layout(display: "flex", direction: "column", gap: "1.5rem"));

    private static VisualPageElement TimelineEntry(
        string date,
        string heading,
        string description) => Element(
        "core.list-item",
        heading,
        children:
        [
            Element(
                "html.time",
                "Timeline date",
                date,
                styles: Styles(
                    ("color", "var(--vp-color-pale-gold, #e8c97a)"),
                    ("font-family", "var(--vp-font-mono, monospace)"),
                    ("font-size", "0.76rem"),
                    ("letter-spacing", "0.08em"))),
            Element("core.heading3", "Event heading", heading, styles: SubheadingStyles()),
            Element("core.paragraph", "Event context", description)
        ],
        styles: CardStyles(("border-left", "3px solid #c8963e")),
        baseLayout: Layout(display: "flex", direction: "column", gap: "0.6rem", padding: "1.15rem 1.3rem"));

    private static VisualPageElement ImageGallery() => Element(
        "core.section",
        "Image gallery",
        children:
        [
            HeadingGroup(
                "Visual record",
                "Images in context",
                "Use captions to explain provenance, date, location, and relevance—not merely what is visible."),
            Element(
                "core.grid",
                "Gallery grid",
                children:
                [
                    ImageFigure("Image one", "Caption, creator, date, collection, and source."),
                    ImageFigure("Image two", "Caption, creator, date, collection, and source."),
                    ImageFigure("Image three", "Caption, creator, date, collection, and source.")
                ],
                baseLayout: Layout(
                    display: "grid",
                    columns: "repeat(auto-fit, minmax(14rem, 1fr))",
                    gap: "1rem"))
        ],
        attributes: Attributes(("id", "images"), ("aria-label", "Image gallery")),
        baseLayout: Layout(display: "flex", direction: "column", gap: "1.4rem"));

    private static VisualPageElement ImageFigure(
        string title,
        string caption) => Element(
        "core.figure",
        title,
        children:
        [
            Element(
                "core.image",
                $"{title} image",
                attributes: Attributes(
                    ("src", "assets/placeholder.svg"),
                    ("alt", $"Describe {title.ToLowerInvariant()}"),
                    ("loading", "lazy")),
                styles: Styles(
                    ("aspect-ratio", "16 / 10"),
                    ("background", "#0b1226"),
                    ("border-radius", "10px"),
                    ("height", "auto"),
                    ("object-fit", "cover"),
                    ("width", "100%"))),
            Element(
                "core.caption",
                $"{title} caption",
                caption,
                styles: Styles(
                    ("color", "var(--vp-color-muted, #8a94a6)"),
                    ("font-size", "0.82rem"),
                    ("line-height", "1.6")))
        ],
        styles: CardStyles(("margin", "0")),
        baseLayout: Layout(display: "flex", direction: "column", gap: "0.8rem", padding: "0.9rem"));

    private static VisualPageElement AudioTranscript() => Element(
        "core.section",
        "Audio and transcript",
        children:
        [
            HeadingGroup(
                "Listen",
                "Recorded example",
                "Introduce the speaker, setting, date, language or sound, and the reason this recording matters."),
            Element(
                "core.figure",
                "Audio recording",
                children:
                [
                    Element(
                        "core.audio",
                        "Audio player",
                        attributes: Attributes(("controls", "true"), ("preload", "metadata")),
                        styles: Styles(("width", "100%"))),
                    Element(
                        "core.caption",
                        "Audio caption",
                        "Recording title · speaker or creator · date · source and rights information")
                ],
                styles: CardStyles(),
                baseLayout: Layout(display: "flex", direction: "column", gap: "0.8rem", padding: "1rem")),
            Element(
                "html.details",
                "Audio transcript",
                children:
                [
                    Element("html.summary", "Transcript control", "Read the transcript"),
                    Element(
                        "core.paragraph",
                        "Transcript text",
                        "[00:00] Add a time-coded transcript or a faithful textual alternative here.")
                ],
                styles: CardStyles(),
                baseLayout: Layout(display: "block", padding: "1rem"))
        ],
        attributes: Attributes(("id", "audio"), ("aria-label", "Audio and transcript")),
        baseLayout: Layout(display: "flex", direction: "column", gap: "1.25rem"));

    private static VisualPageElement VideoFeature() => Element(
        "core.section",
        "Video feature",
        children:
        [
            HeadingGroup(
                "Watch",
                "Video evidence or demonstration",
                "Explain what the video contributes and identify the creator, date, context, and rights."),
            Element(
                "core.figure",
                "Video and caption",
                children:
                [
                    Element(
                        "core.video",
                        "Video player",
                        attributes: Attributes(
                            ("controls", "true"),
                            ("preload", "metadata"),
                            ("poster", "assets/placeholder.svg")),
                        styles: Styles(
                            ("aspect-ratio", "16 / 9"),
                            ("background", "#070d1a"),
                            ("border-radius", "10px"),
                            ("width", "100%"))),
                    Element(
                        "core.caption",
                        "Video caption",
                        "Video title · creator · date · source · captions or transcript status")
                ],
                styles: CardStyles(),
                baseLayout: Layout(display: "flex", direction: "column", gap: "0.8rem", padding: "1rem")),
            Element(
                "html.details",
                "Video transcript",
                children:
                [
                    Element("html.summary", "Transcript control", "Read the video transcript"),
                    Element("core.paragraph", "Transcript text", "Add a time-coded transcript or detailed visual description.")
                ],
                styles: CardStyles(),
                baseLayout: Layout(display: "block", padding: "1rem"))
        ],
        attributes: Attributes(("id", "video"), ("aria-label", "Video feature")),
        baseLayout: Layout(display: "flex", direction: "column", gap: "1.25rem"));

    private static VisualPageElement DataEvidenceSection(
        string heading = "Graph and source data") => Element(
        "core.section",
        "Graph and data table",
        children:
        [
            HeadingGroup(
                "Evidence",
                heading,
                "State the measure, units, timeframe, sample, and source before interpreting the pattern."),
            Element(
                "core.figure",
                "Data visualization",
                children:
                [
                    Element(
                        "html.canvas",
                        "Chart canvas",
                        "Chart fallback: describe the trend and use the table below for exact values.",
                        attributes: Attributes(
                            ("id", "archive-chart"),
                            ("width", "960"),
                            ("height", "480"),
                            ("role", "img"),
                            ("aria-label", "Describe the graph and its central pattern")),
                        styles: Styles(
                            ("background", "linear-gradient(180deg, #101b35, #0b1226)"),
                            ("border", "1px solid rgba(138, 159, 193, 0.24)"),
                            ("border-radius", "10px"),
                            ("min-height", "18rem"),
                            ("width", "100%"))),
                    Element(
                        "core.caption",
                        "Chart caption",
                        "Figure: name the variables, units, date range, and source. Summarize the principal pattern without overstating causation.")
                ],
                styles: Styles(("margin", "0")),
                baseLayout: Layout(display: "flex", direction: "column", gap: "0.8rem")),
            DataTable(),
            FactCallout(
                "How to read this evidence",
                "Explain the comparison, uncertainty, baseline, and any transformation applied to the source data.")
        ],
        attributes: Attributes(("id", "evidence"), ("aria-label", "Graph and source data")),
        baseLayout: Layout(display: "flex", direction: "column", gap: "1.4rem"));

    private static VisualPageElement DataTable() => Element(
        "html.table",
        "Accessible source data",
        children:
        [
            Element(
                "html.caption",
                "Table caption",
                "Source values used by the graph",
                styles: Styles(
                    ("color", "var(--vp-color-silver, #bcc5d0)"),
                    ("font-weight", "600"),
                    ("padding", "0.8rem"),
                    ("text-align", "left"))),
            Element(
                "html.thead",
                "Column headings",
                children:
                [
                    Element(
                        "html.tr",
                        "Heading row",
                        children:
                        [
                            Element("html.th", "Period heading", "Period", attributes: Attributes(("scope", "col")), styles: TableHeaderCellStyles()),
                            Element("html.th", "Measure heading", "Measure", attributes: Attributes(("scope", "col")), styles: TableHeaderCellStyles()),
                            Element("html.th", "Context heading", "Context", attributes: Attributes(("scope", "col")), styles: TableHeaderCellStyles())
                        ])
                ]),
            Element(
                "html.tbody",
                "Table values",
                children:
                [
                    TableRow("Sample A", "42", "Describe the unit or observation"),
                    TableRow("Sample B", "57", "Describe the unit or observation"),
                    TableRow("Sample C", "49", "Describe the unit or observation")
                ])
        ],
        styles: Styles(
            ("border-collapse", "collapse"),
            ("font-size", "0.9rem"),
            ("width", "100%")));

    private static VisualPageElement TableRow(
        string first,
        string second,
        string third) => Element(
        "html.tr",
        "Data row",
        children:
        [
            Element("html.th", "Row heading", first, attributes: Attributes(("scope", "row")), styles: TableHeaderCellStyles()),
            Element("html.td", "Measure value", second, styles: TableCellStyles()),
            Element("html.td", "Context value", third, styles: TableCellStyles())
        ]);

    private static VisualPageElement SourcesSection(
        string heading = "Sources and further reading") => Element(
        "core.section",
        "Sources and further reading",
        children:
        [
            HeadingGroup(
                "References",
                heading,
                "Prefer primary sources and stable scholarly or institutional references. Note uncertainty and access dates where appropriate."),
            Element(
                "core.ordered-list",
                "Reference list",
                children: [CitationItem(), CitationItem("Source two"), CitationItem("Source three")],
                baseLayout: Layout(display: "flex", direction: "column", gap: "0.8rem"))
        ],
        attributes: Attributes(("id", "sources"), ("aria-label", "Sources and further reading")),
        baseLayout: Layout(display: "flex", direction: "column", gap: "1.25rem"));

    private static VisualPageElement CitationItem(string name = "Source one") => Element(
        "core.list-item",
        name,
        children:
        [
            Element(
                "core.link",
                "Source title",
                "Source title",
                attributes: Attributes(("href", "https://example.com/source")),
                styles: Styles(("color", "var(--vp-color-pale-gold, #e8c97a)"))),
            Element(
                "core.paragraph",
                "Source details",
                "Author or organization · publication · date · access note",
                styles: Styles(
                    ("color", "var(--vp-color-muted, #8a94a6)"),
                    ("font-size", "0.82rem"),
                    ("margin", "0.3rem 0 0")))
        ],
        styles: Styles(("padding-left", "0.35rem")));

    private static VisualPageElement RelatedTopics() => Element(
        "core.section",
        "Related topics",
        children:
        [
            HeadingGroup(
                "Continue exploring",
                "Related Archive topics",
                "Connect this page to adjacent histories, concepts, people, languages, or technical guides."),
            Element(
                "atlas.card-grid",
                "Related topic cards",
                children: [EditorialCard("Related topic one"), EditorialCard("Related topic two"), EditorialCard("Related topic three")])
        ],
        attributes: Attributes(("aria-label", "Related topics")),
        styles: Styles(("border-top", "1px solid rgba(138, 159, 193, 0.24)")),
        baseLayout: Layout(
            display: "flex",
            direction: "column",
            gap: "1.5rem",
            padding: "clamp(3rem, 7vw, 6rem) clamp(1.25rem, 8vw, 8rem)"));

    private static VisualPageElement EditorialCard(
        string heading = "Related topic") => Element(
        "atlas.card",
        heading,
        children:
        [
            Element("core.paragraph", "Card label", "Archive topic", styles: KickerStyles()),
            Element("core.heading3", "Card heading", heading, styles: SubheadingStyles()),
            Element("core.paragraph", "Card summary", "Add a concise explanation of what the reader will discover."),
            Element(
                "core.link",
                "Card link",
                "Explore topic",
                attributes: Attributes(("href", "#")),
                styles: Styles(
                    ("color", "var(--vp-color-pale-gold, #e8c97a)"),
                    ("font-family", "var(--vp-font-mono, monospace)"),
                    ("font-size", "0.8rem"),
                    ("text-decoration", "none")))
        ],
        styles: CardStyles(),
        baseLayout: Layout(display: "flex", direction: "column", gap: "0.75rem", padding: "1.2rem"));

    private static VisualPageElement FactCallout(
        string heading = "Key context",
        string text = "Use this space for a definition, caution, misconception, source limitation, or detail readers should keep in mind.") => Element(
        "html.aside",
        "Atlas fact callout",
        children:
        [
            Element("core.paragraph", "Callout label", "Field note", styles: KickerStyles()),
            Element("core.heading3", "Callout heading", heading, styles: SubheadingStyles()),
            Element("core.paragraph", "Callout text", text)
        ],
        attributes: Attributes(("aria-label", heading)),
        styles: CardStyles(("border-left", "3px solid #c8963e")),
        baseLayout: Layout(display: "flex", direction: "column", gap: "0.65rem", padding: "1.2rem 1.35rem"));

    private static VisualPageElement PullQuote(
        string quote = "Place a short, consequential quotation here—one that adds a voice or piece of evidence the surrounding prose cannot replace.",
        string attribution = "Speaker, author, or source · date") => Element(
        "core.figure",
        "Atlas pull quote",
        children:
        [
            Element(
                "core.quote",
                "Quotation",
                quote,
                styles: Styles(
                    ("color", "var(--vp-color-heading, #edf3ff)"),
                    ("font-family", "var(--vp-font-display, Georgia, serif)"),
                    ("font-size", "clamp(1.35rem, 3vw, 2rem)"),
                    ("line-height", "1.45"),
                    ("margin", "0"))),
            Element(
                "core.caption",
                "Attribution",
                attribution,
                styles: Styles(
                    ("color", "var(--vp-color-pale-gold, #e8c97a)"),
                    ("font-family", "var(--vp-font-mono, monospace)"),
                    ("font-size", "0.78rem")))
        ],
        styles: Styles(
            ("border-left", "3px solid #c8963e"),
            ("margin", "1.5rem 0"),
            ("padding", "1rem 0 1rem clamp(1.25rem, 4vw, 2.25rem)")),
        baseLayout: Layout(display: "flex", direction: "column", gap: "0.8rem"));

    private static VisualPageElement GlossarySection() => Element(
        "core.section",
        "Glossary",
        children:
        [
            HeadingGroup(
                "Vocabulary",
                "Words in context",
                "Include pronunciation, grammatical or cultural context, and a usage example where useful."),
            Element(
                "html.dl",
                "Glossary terms",
                children: [GlossaryEntry(), GlossaryEntry("Second term"), GlossaryEntry("Third term")],
                baseLayout: Layout(display: "grid", columns: "minmax(0, 1fr)", gap: "0.8rem"))
        ],
        attributes: Attributes(("id", "glossary"), ("aria-label", "Glossary")),
        baseLayout: Layout(display: "flex", direction: "column", gap: "1.25rem"));

    private static VisualPageElement GlossaryEntry(string term = "Term or expression") => Element(
        "core.container",
        $"{term} glossary entry",
        children:
        [
            Element(
                "html.dt",
                "Glossary term",
                term,
                styles: Styles(
                    ("color", "var(--vp-color-heading, #edf3ff)"),
                    ("font-weight", "600"),
                    ("letter-spacing", "0.04em"))),
            Element(
                "html.dd",
                "Glossary definition",
                "Pronunciation or metadata · Define the term in context and add a short usage example.",
                styles: Styles(
                    ("color", "var(--vp-color-silver, #bcc5d0)"),
                    ("margin", "0.35rem 0 0")))
        ],
        styles: CardStyles(),
        baseLayout: Layout(display: "flex", direction: "column", gap: "0.25rem", padding: "1rem"));

    private static VisualPageElement MechanismCards() => Element(
        "core.section",
        "Mechanisms",
        children:
        [
            HeadingGroup(
                "Mechanisms",
                "Processes that shape the pattern",
                "Separate mechanisms from outcomes and show how each one would leave observable evidence."),
            Element(
                "atlas.card-grid",
                "Mechanism cards",
                children:
                [
                    MechanismCard("Variation", "Describe the relevant differences among individuals or populations."),
                    MechanismCard("Inheritance", "Explain how the trait or information passes across generations."),
                    MechanismCard("Selection and drift", "Distinguish directional pressures from stochastic change.")
                ])
        ],
        attributes: Attributes(("id", "mechanisms"), ("aria-label", "Evolutionary mechanisms")),
        baseLayout: Layout(display: "flex", direction: "column", gap: "1.25rem"));

    private static VisualPageElement MechanismCard(
        string heading,
        string text) => Element(
        "atlas.card",
        heading,
        children:
        [
            Element("core.heading3", "Mechanism heading", heading, styles: SubheadingStyles()),
            Element("core.paragraph", "Mechanism explanation", text)
        ],
        styles: CardStyles(),
        baseLayout: Layout(display: "flex", direction: "column", gap: "0.65rem", padding: "1.15rem"));

    private static VisualPageElement ProcessSteps() => Element(
        "core.section",
        "Process steps",
        children:
        [
            HeadingGroup(
                "Process",
                "From variation to observed pattern",
                "Use these steps to make causal sequence and timescale explicit."),
            Element(
                "core.ordered-list",
                "Process sequence",
                children:
                [
                    TimelineEntry("01", "Variation appears", "Identify the source and scale of variation."),
                    TimelineEntry("02", "Conditions filter outcomes", "Describe the environment and competing pressures."),
                    TimelineEntry("03", "Frequencies change", "Connect individual outcomes to population-level change.")
                ],
                styles: Styles(("list-style", "none"), ("margin", "0"), ("padding", "0")),
                baseLayout: Layout(display: "flex", direction: "column", gap: "0.9rem"))
        ],
        baseLayout: Layout(display: "flex", direction: "column", gap: "1.25rem"));

    private static VisualPageElement Prerequisites() => FactCallout(
        "Prerequisites and versions",
        "List the operating system, SDK or runtime version, packages, tools, permissions, and starting files required to reproduce the guide.");

    private static VisualPageElement TutorialSteps() => Element(
        "core.section",
        "Tutorial steps",
        children:
        [
            HeadingGroup(
                "Build",
                "A reproducible path",
                "Each step should produce a visible checkpoint that readers can verify before continuing."),
            Element(
                "core.ordered-list",
                "Build steps",
                children:
                [
                    TimelineEntry("Step 1", "Create the smallest working project", "Show the command and explain every generated file used later."),
                    TimelineEntry("Step 2", "Add the core behavior", "Introduce one concept at a time and keep the example runnable."),
                    TimelineEntry("Step 3", "Verify the result", "Provide the expected output and a focused diagnostic command.")
                ],
                styles: Styles(("list-style", "none"), ("margin", "0"), ("padding", "0")),
                baseLayout: Layout(display: "flex", direction: "column", gap: "0.9rem"))
        ],
        attributes: Attributes(("id", "build"), ("aria-label", "Build steps")),
        baseLayout: Layout(display: "flex", direction: "column", gap: "1.25rem"));

    private static VisualPageElement CodeListing() => Element(
        "core.figure",
        "Atlas code listing",
        children:
        [
            Element(
                "core.caption",
                "Code metadata",
                "Program.cs · C# · .NET 10",
                styles: Styles(
                    ("color", "var(--vp-color-pale-gold, #e8c97a)"),
                    ("font-family", "var(--vp-font-mono, monospace)"),
                    ("font-size", "0.76rem"),
                    ("letter-spacing", "0.06em"))),
            Element(
                "core.code",
                "Code sample",
                "var message = \"Hello from Atlas Archive\";\nConsole.WriteLine(message);",
                styles: Styles(
                    ("background", "#070d1a"),
                    ("border", "1px solid rgba(138, 159, 193, 0.24)"),
                    ("border-radius", "10px"),
                    ("color", "#e8ecf1"),
                    ("font-family", "var(--vp-font-mono, monospace)"),
                    ("font-size", "0.88rem"),
                    ("line-height", "1.65"),
                    ("margin", "0"),
                    ("overflow-x", "auto"),
                    ("padding", "1rem"),
                    ("white-space", "pre"))),
            Element(
                "core.paragraph",
                "Code explanation",
                "Explain what the code does, why this version was chosen, and which values readers are expected to change.",
                styles: Styles(("color", "var(--vp-color-silver, #bcc5d0)")))
        ],
        styles: CardStyles(("margin", "1rem 0")),
        baseLayout: Layout(display: "flex", direction: "column", gap: "0.8rem", padding: "1rem"));

    private static VisualPageElement ApiReferenceTable() => Element(
        "core.section",
        "API reference",
        children:
        [
            HeadingGroup(
                "Reference",
                "Important inputs and outputs",
                "Use the table as a compact reference after explaining each concept in prose."),
            Element(
                "html.table",
                "API table",
                children:
                [
                    Element(
                        "html.caption",
                        "API table caption",
                        "Key members used in this guide",
                        styles: Styles(
                            ("color", "var(--vp-color-silver, #bcc5d0)"),
                            ("font-weight", "600"),
                            ("padding", "0.8rem"),
                            ("text-align", "left"))),
                    Element(
                        "html.thead",
                        "API headings",
                        children:
                        [
                            Element(
                                "html.tr",
                                "API heading row",
                                children:
                                [
                                    Element("html.th", "Member heading", "Member", attributes: Attributes(("scope", "col")), styles: TableHeaderCellStyles()),
                                    Element("html.th", "Purpose heading", "Purpose", attributes: Attributes(("scope", "col")), styles: TableHeaderCellStyles()),
                                    Element("html.th", "Notes heading", "Notes", attributes: Attributes(("scope", "col")), styles: TableHeaderCellStyles())
                                ])
                        ]),
                    Element(
                        "html.tbody",
                        "API rows",
                        children:
                        [
                            TableRow("MethodName", "Performs the operation", "State preconditions and side effects"),
                            TableRow("OptionName", "Controls a behavior", "Include default and supported values")
                        ])
                ],
                styles: Styles(("border-collapse", "collapse"), ("width", "100%")))
        ],
        attributes: Attributes(("id", "explain"), ("aria-label", "API reference")),
        baseLayout: Layout(display: "flex", direction: "column", gap: "1.2rem"));

    private static VisualPageElement Troubleshooting() => Element(
        "core.section",
        "Troubleshooting",
        children:
        [
            HeadingGroup(
                "Troubleshoot",
                "Common failure states",
                "Show the symptom, likely cause, exact check, and smallest safe correction."),
            Element(
                "html.details",
                "First troubleshooting item",
                children:
                [
                    Element("html.summary", "Problem summary", "The command fails before the app starts"),
                    Element("core.paragraph", "Problem solution", "Record the complete error, verify the installed SDK, and compare it with the guide's version assumptions.")
                ],
                styles: CardStyles(),
                baseLayout: Layout(display: "block", padding: "1rem")),
            Element(
                "html.details",
                "Second troubleshooting item",
                children:
                [
                    Element("html.summary", "Problem summary", "The output differs from the example"),
                    Element("core.paragraph", "Problem solution", "Check the input, environment, and preceding checkpoint before changing the implementation.")
                ],
                styles: CardStyles(),
                baseLayout: Layout(display: "block", padding: "1rem"))
        ],
        attributes: Attributes(("id", "troubleshoot"), ("aria-label", "Troubleshooting")),
        baseLayout: Layout(display: "flex", direction: "column", gap: "0.9rem"));

    private static Dictionary<string, string> KickerStyles() => Styles(
        ("color", "rgba(176, 193, 219, 0.84)"),
        ("font-size", "0.72rem"),
        ("font-weight", "500"),
        ("letter-spacing", "0.34em"),
        ("margin", "0"),
        ("text-transform", "uppercase"));

    private static Dictionary<string, string> HeadingStyles() => Styles(
        ("color", "var(--vp-color-heading, #edf3ff)"),
        ("font-size", "clamp(1.65rem, 4vw, 2.6rem)"),
        ("font-weight", "300"),
        ("letter-spacing", "0.1em"),
        ("line-height", "1.25"),
        ("margin", "0"),
        ("text-transform", "uppercase"));

    private static Dictionary<string, string> SubheadingStyles() => Styles(
        ("color", "var(--vp-color-heading, #edf3ff)"),
        ("font-size", "1rem"),
        ("font-weight", "600"),
        ("letter-spacing", "0.1em"),
        ("line-height", "1.45"),
        ("margin", "0"),
        ("text-transform", "uppercase"));

    private static Dictionary<string, string> LeadStyles() => Styles(
        ("color", "var(--vp-color-silver, #bcc5d0)"),
        ("font-size", "1.05rem"),
        ("line-height", "1.75"),
        ("margin", "0"),
        ("max-width", "68ch"));

    private static Dictionary<string, string> TableHeaderCellStyles() => Styles(
        ("border-bottom", "1px solid rgba(138, 159, 193, 0.38)"),
        ("color", "var(--vp-color-heading, #edf3ff)"),
        ("font-size", "0.78rem"),
        ("font-weight", "600"),
        ("letter-spacing", "0.06em"),
        ("padding", "0.75rem 0.8rem"),
        ("text-align", "left"),
        ("text-transform", "uppercase"));

    private static Dictionary<string, string> TableCellStyles() => Styles(
        ("border-bottom", "1px solid rgba(138, 159, 193, 0.18)"),
        ("color", "var(--vp-color-silver, #bcc5d0)"),
        ("padding", "0.75rem 0.8rem"),
        ("vertical-align", "top"));

    private static Dictionary<string, string> CardStyles(
        params (string Name, string Value)[] additions)
    {
        var styles = Styles(
            ("background", "linear-gradient(160deg, rgba(16, 27, 53, 0.78), rgba(10, 18, 34, 0.86))"),
            ("border", "1px solid rgba(138, 159, 193, 0.24)"),
            ("border-radius", "var(--vp-radius-card, 14.4px)"),
            ("box-shadow", "var(--vp-shadow-soft, 0 14px 36px rgba(0, 0, 0, 0.28))"));
        foreach (var addition in additions)
        {
            styles[addition.Name] = addition.Value;
        }
        return styles;
    }

    private static VisualPageElement Element(
        string descriptorId,
        string name,
        string text = "",
        IReadOnlyList<VisualPageElement>? children = null,
        IReadOnlyDictionary<string, string>? attributes = null,
        IReadOnlyDictionary<string, string>? styles = null,
        VisualPageLayoutSpec? baseLayout = null,
        VisualPageLayoutSpec? largeLayout = null)
    {
        var element = new VisualPageElement
        {
            DescriptorId = descriptorId,
            Name = name,
            Text = text,
            Children = children?.ToList() ?? [],
            Attributes = attributes is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(attributes, StringComparer.Ordinal),
            Styles = styles is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(styles, StringComparer.Ordinal)
        };
        if (baseLayout is not null)
        {
            element.Layout[VisualPageBreakpoint.Base] = baseLayout;
        }
        if (largeLayout is not null)
        {
            element.Layout[VisualPageBreakpoint.Large] = largeLayout;
        }
        return element;
    }

    private static VisualPageLayoutSpec Layout(
        string display = "",
        string direction = "",
        string columns = "",
        string gap = "",
        string padding = "")
        => new()
        {
            Display = display,
            FlexDirection = direction,
            GridTemplateColumns = columns,
            Gap = gap,
            Padding = padding
        };

    private static Dictionary<string, string> Attributes(
        params (string Name, string Value)[] values) => values.ToDictionary(
        value => value.Name,
        value => value.Value,
        StringComparer.Ordinal);

    private static Dictionary<string, string> Styles(
        params (string Name, string Value)[] values) => values.ToDictionary(
        value => value.Name,
        value => value.Value,
        StringComparer.Ordinal);

    private static string Slug(string value) => string.Join(
        '-',
        value.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => new string(part.Where(char.IsLetterOrDigit).ToArray()))
            .Where(part => part.Length > 0));
}
