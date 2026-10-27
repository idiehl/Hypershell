using Hypershell.Models;

namespace Hypershell.Services;

public static class VisualPageTemplateTaxonomy
{
    public const string AllCategories = "All categories";

    public static string ResolveCategoryGroup(
        VisualPageTemplateDocument template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return StudioTemplateRoleTaxonomy.Resolve(template);
    }

    public static string ResolveCategoryGroup(
        string? descriptorCategory,
        string scope) => (descriptorCategory ?? string.Empty) switch
        {
            "Navigation" => "Navigation",
            "Forms" or "Input controls" or "Validation" => "Forms",
            "Media" => "Media", "Tables" => "Data",
            "Typography" or "Lists" => "Content",
            "Layout" => "Layout",
            _ => "Content"
        };

    private static bool ContainsAny(
        string? value,
        params string[] terms) => terms.Any(term =>
        (value ?? string.Empty).Contains(
            term,
            StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// General-purpose starters that are intentionally independent of Atlas branding.
/// The catalog is organized by user intent first, then by the concrete pattern.
/// </summary>
public static class VisualPageCommonTemplateCatalog
{
    public const string LibraryName = "Hyperpage starters";

    public static IReadOnlyList<string> CategoryGroups { get; } =
        StudioTemplateRoleTaxonomy.Categories;

    public static IReadOnlyList<VisualPageTemplateDocument> CreateTemplates() =>
    [
        PageTemplate(
            "common.page.landing.v1",
            "Conversion landing page",
            "Marketing & conversion",
            "Landing pages",
            "A complete campaign or product landing page with navigation, hero, benefits, proof, and a final action.",
            "Use when one audience should be guided toward one primary outcome.",
            "Landing Page",
            "/landing-page",
            PageRoot(
                TopNavigation(),
                HeroSection(),
                FeatureGrid(),
                TestimonialSection(),
                CallToActionSection(),
                FooterSection())),
        PageTemplate(
            "common.page.directory.v1",
            "Indexed directory",
            "Discovery & collections",
            "Indexes & directories",
            "Searchable collection page with an introduction, filters, responsive result cards, and pagination.",
            "Use for resources, people, products, articles, locations, or any growing collection.",
            "Directory",
            "/directory",
            PageRoot(
                TopNavigation(),
                CompactPageHeader("Browse the collection", "Help visitors find the right item quickly."),
                SearchFilterSection(),
                DirectoryGrid(),
                PaginationElement(),
                FooterSection())),
        PageTemplate(
            "common.page.detail.v1",
            "Content detail page",
            "Content & publishing",
            "Detail & sub-pages",
            "Focused sub-page with breadcrumbs, an article body, supporting facts, related links, and a next action.",
            "Use for an article, service, product, case study, profile, or other single record.",
            "Detail Page",
            "/detail-page",
            PageRoot(
                TopNavigation(),
                BreadcrumbElement(),
                DetailContent(),
                CallToActionSection(),
                FooterSection())),
        PageTemplate(
            "common.page.documentation.v1",
            "Documentation page",
            "Content & publishing",
            "Documentation",
            "Documentation shell with top navigation, a persistent topic list, readable main content, and an on-page callout.",
            "Use for guides, product documentation, policies, onboarding, or reference material.",
            "Documentation",
            "/docs/getting-started",
            PageRoot(
                TopNavigation(),
                SidebarShell(
                    "Documentation topics",
                    MainContentPanel(
                        "Getting started",
                        "Explain the goal, prerequisites, and the shortest successful path.",
                        AlertElement())),
                FooterSection())),
        PageTemplate(
            "common.page.dashboard.v1",
            "Dashboard overview",
            "Applications & accounts",
            "Dashboards",
            "Application overview with navigation, status metrics, recent activity, and task panels.",
            "Use for signed-in product areas, operations views, or internal tools.",
            "Dashboard",
            "/dashboard",
            PageRoot(
                TopNavigation(),
                CompactPageHeader("Dashboard", "A concise view of what needs attention."),
                StatisticsSection(),
                DashboardPanels(),
                FooterSection())),
        PageTemplate(
            "common.page.pricing.v1",
            "Pricing and plans",
            "Marketing & conversion",
            "Pricing pages",
            "Plan comparison page with a clear introduction, three tiers, common questions, and a final action.",
            "Use when visitors need to compare packages or service levels.",
            "Pricing",
            "/pricing",
            PageRoot(
                TopNavigation(),
                CompactPageHeader("Plans that scale with you", "Choose the level that fits your current needs."),
                PricingSection(),
                FaqSection(),
                CallToActionSection(),
                FooterSection())),
        PageTemplate(
            "common.page.portfolio.v1",
            "Portfolio and gallery",
            "Discovery & collections",
            "Portfolios & galleries",
            "Image-led portfolio with an introduction, project gallery, services summary, and contact action.",
            "Use for creative work, project showcases, case studies, or visual collections.",
            "Portfolio",
            "/portfolio",
            PageRoot(
                TopNavigation(),
                HeroSection("Selected work, clearly presented", "Introduce the point of view behind the collection."),
                PortfolioGrid(),
                FeatureGrid("Capabilities", "Summarize the services or disciplines represented in the work."),
                CallToActionSection("Have a project in mind?", "Start a conversation"),
                FooterSection())),
        PageTemplate(
            "common.page.contact.v1",
            "Contact and inquiry",
            "Business & contact",
            "Contact pages",
            "Contact page with expectations, direct contact details, and an accessible inquiry form.",
            "Use when visitors need a clear route to a person, team, or support channel.",
            "Contact",
            "/contact",
            PageRoot(
                TopNavigation(),
                CompactPageHeader("Let's talk", "Set expectations for response time and the information you need."),
                ContactSection(),
                FooterSection())),

        SectionTemplate(
            "common.section.top-navigation.v1",
            "Top navigation bar",
            "Navigation",
            "Primary navigation",
            "Responsive brand, primary links, and one emphasized action.",
            "Place near the top of a standalone page.",
            TopNavigation()),
        SectionTemplate(
            "common.section.sidebar-shell.v1",
            "Sidebar content shell",
            "Layout & structure",
            "Sidebar layouts",
            "Responsive two-column shell with a labeled side panel and a primary content region.",
            "Use for documentation, filters, account navigation, or related information.",
            SidebarShell("Section navigation", MainContentPanel())),
        SectionTemplate(
            "common.section.main-content-panel.v1",
            "Main content box",
            "Layout & structure",
            "Content containers",
            "Readable, centered content surface with heading, introduction, and supporting body copy.",
            "Use as the main reading or task area inside a page shell.",
            MainContentPanel()),
        SectionTemplate(
            "common.section.hero.v1",
            "Universal hero",
            "Content blocks",
            "Hero sections",
            "Responsive introductory section with headline, supporting copy, actions, and a media placeholder.",
            "Use once near the beginning of a landing or overview page.",
            HeroSection()),
        SectionTemplate(
            "common.section.search-filter.v1",
            "Search and filter bar",
            "Discovery & collections",
            "Search & filters",
            "Accessible search field, category selector, and apply action in a responsive toolbar.",
            "Place above a list, table, gallery, or directory.",
            SearchFilterSection()),
        SectionTemplate(
            "common.section.feature-grid.v1",
            "Feature grid",
            "Content blocks",
            "Feature summaries",
            "Three concise benefit or capability cards in a responsive grid.",
            "Use to make a small set of benefits easy to scan.",
            FeatureGrid()),
        SectionTemplate(
            "common.section.statistics.v1",
            "Statistics strip",
            "Data display",
            "Key metrics",
            "Four responsive metric cards with values and plain-language labels.",
            "Use for a small, meaningful set of headline measures.",
            StatisticsSection()),
        SectionTemplate(
            "common.section.testimonials.v1",
            "Testimonials and proof",
            "Identity & people",
            "Testimonials",
            "Two accessible quotations with concise attribution.",
            "Use only with authentic, approved quotations and attribution.",
            TestimonialSection()),
        SectionTemplate(
            "common.section.pricing.v1",
            "Pricing cards",
            "Marketing & conversion",
            "Pricing comparisons",
            "Three plan cards with price, included items, and an action.",
            "Use when plan differences can be expressed in a short comparison.",
            PricingSection()),
        SectionTemplate(
            "common.section.faq.v1",
            "Common questions",
            "Content blocks",
            "Frequently asked questions",
            "Heading and three native disclosure rows for concise answers.",
            "Use for questions that otherwise interrupt the main page flow.",
            FaqSection()),
        SectionTemplate(
            "common.section.action-banner.v1",
            "Action banner",
            "Marketing & conversion",
            "Calls to action",
            "High-contrast heading, supporting copy, and one primary action.",
            "Use when the page has one clear next step.",
            CallToActionSection()),
        SectionTemplate(
            "common.section.footer.v1",
            "Site footer",
            "Navigation",
            "Footers",
            "Site name, utility links, and a concise copyright placeholder.",
            "Place once at the end of a standalone page.",
            FooterSection()),

        ElementTemplate(
            "common.element.content-card.v1",
            "Content card",
            "Content blocks",
            "Cards",
            "Reusable title, summary, metadata, and link card.",
            "Use inside a responsive grid or related-content list.",
            ContentCard(1)),
        ElementTemplate(
            "common.element.alert.v1",
            "Information callout",
            "Feedback & status",
            "Alerts & callouts",
            "Labeled status or guidance box with accessible alert semantics.",
            "Use for important context, status, warnings, or next steps.",
            AlertElement()),
        ElementTemplate(
            "common.element.button-group.v1",
            "Button group",
            "Marketing & conversion",
            "Action groups",
            "Primary and secondary actions with responsive wrapping.",
            "Use when one action is primary and one is a lower-priority alternative.",
            ButtonGroup()),
        ElementTemplate(
            "common.element.badge.v1",
            "Status badge",
            "Feedback & status",
            "Badges & labels",
            "Compact status or category label.",
            "Use for short metadata; do not rely on color alone.",
            BadgeElement()),
        ElementTemplate(
            "common.element.pagination.v1",
            "Pagination controls",
            "Navigation",
            "Pagination",
            "Previous, numbered-page, and next links with an accessible label.",
            "Use for a collection split into stable pages.",
            PaginationElement()),
        ElementTemplate(
            "common.element.breadcrumb.v1",
            "Breadcrumb trail",
            "Navigation",
            "Breadcrumbs",
            "Compact hierarchy trail with an accessible navigation label.",
            "Use on deeper pages where the parent hierarchy helps orientation.",
            BreadcrumbElement()),
        ElementTemplate(
            "common.element.metadata.v1",
            "Content metadata row",
            "Content & publishing",
            "Metadata",
            "Responsive row for date, author, category, or reading-time labels.",
            "Use directly below an article or detail-page heading.",
            MetadataElement()),
        ElementTemplate(
            "common.element.profile.v1",
            "Profile summary",
            "Identity & people",
            "Profiles",
            "Avatar placeholder, name, role, short biography, and profile link.",
            "Use for authors, team members, speakers, or directory entries.",
            ProfileElement())
    ];

    private static VisualPageTemplateDocument PageTemplate(
        string id,
        string name,
        string categoryGroup,
        string category,
        string description,
        string usageHint,
        string pageName,
        string route,
        VisualPageElement root) => Template(
            id, name, "page", categoryGroup, category, description, usageHint,
            root,
            pageName,
            route);

    private static VisualPageTemplateDocument SectionTemplate(
        string id,
        string name,
        string categoryGroup,
        string category,
        string description,
        string usageHint,
        VisualPageElement root) => Template(
            id, name, "section", categoryGroup, category, description,
            usageHint, root);

    private static VisualPageTemplateDocument ElementTemplate(
        string id,
        string name,
        string categoryGroup,
        string category,
        string description,
        string usageHint,
        VisualPageElement root) => Template(
            id, name, "element", categoryGroup, category, description,
            usageHint, root);

    private static VisualPageTemplateDocument Template(
        string id,
        string name,
        string scope,
        string categoryGroup,
        string category,
        string description,
        string usageHint,
        VisualPageElement root,
        string pageName = "",
        string route = "") => new()
        {
            TemplateId = id,
            Name = name,
            Description = description,
            Scope = scope,
            Library = LibraryName,
            CategoryGroup = categoryGroup,
            Category = category,
            UsageHint = usageHint,
            Tags =
            [
                scope,
                categoryGroup.ToLowerInvariant(),
                category.ToLowerInvariant(),
                "responsive",
                "accessible"
            ],
            SuggestedPageName = pageName,
            SuggestedPageRoute = route,
            SuggestedPageTitle = pageName,
            SuggestedPageDescription = description,
            Root = root
        };

    private static VisualPageElement PageRoot(
        params VisualPageElement[] children) => Element(
            "core.page",
            "Page",
            styles: Styles(
                ("background", "#f7f8fb"),
                ("color", "#18202b"),
                ("font-family", "Inter, Segoe UI, sans-serif"),
                ("min-height", "100vh")),
            layout: Layout("flex", "column", gap: "0"),
            children: children);

    private static VisualPageElement TopNavigation() => Element(
        "html.header",
        "Top navigation",
        styles: Styles(
            ("background", "#ffffff"),
            ("border-bottom", "1px solid #dce2ea")),
        layout: Layout(
            "flex", "row", "wrap", gap: "1rem",
            padding: "1rem clamp(1rem, 5vw, 5rem)",
            alignItems: "center", justifyContent: "space-between"),
        children:
        [
            Element(
                "core.link",
                "Brand link",
                "Site name",
                Attributes(("href", "/"), ("aria-label", "Home")),
                Styles(("font-weight", "700"), ("color", "#172033"),
                    ("text-decoration", "none"))),
            Element(
                "core.nav",
                "Primary navigation",
                attributes: Attributes(("aria-label", "Primary navigation")),
                layout: Layout("flex", "row", "wrap", gap: "1rem",
                    alignItems: "center"),
                children:
                [
                    Link("Overview"),
                    Link("Features"),
                    Link("Resources"),
                    Element(
                        "core.button",
                        "Primary navigation action",
                        "Get started",
                        styles: PrimaryActionStyles())
                ])
        ]);

    private static VisualPageElement HeroSection(
        string heading = "A strong reason to choose this",
        string summary = "Explain the outcome in plain language and help the right visitor understand the next step.") => Element(
            "core.section",
            "Hero section",
            attributes: Attributes(("aria-label", "Page introduction")),
            styles: Styles(("background", "linear-gradient(135deg, #edf3ff, #ffffff)")),
            layout: Layout(
                "grid", columns: "minmax(0, 3fr) minmax(16rem, 2fr)",
                gap: "clamp(2rem, 6vw, 5rem)",
                padding: "clamp(3rem, 9vw, 7rem) clamp(1rem, 6vw, 6rem)",
                alignItems: "center"),
            children:
            [
                Element(
                    "core.container",
                    "Hero copy",
                    layout: Layout("flex", "column", gap: "1.25rem"),
                    children:
                    [
                        BadgeElement("A useful category label"),
                        Element("core.heading1", "Hero heading", heading,
                            styles: Styles(("font-size", "clamp(2.5rem, 7vw, 5.5rem)"),
                                ("line-height", "0.98"), ("max-width", "13ch"),
                                ("margin", "0"))),
                        Element("core.paragraph", "Hero summary", summary,
                            styles: Styles(("font-size", "1.15rem"),
                                ("line-height", "1.7"), ("max-width", "58ch"),
                                ("color", "#4a5568"))),
                        ButtonGroup()
                    ]),
                Element(
                    "core.container",
                    "Hero media placeholder",
                    attributes: Attributes(("role", "img"),
                        ("aria-label", "Replace with a relevant visual")),
                    styles: Styles(("min-height", "22rem"),
                        ("border-radius", "1.5rem"),
                        ("background", "linear-gradient(145deg, #274c77, #6c8db8)"),
                        ("box-shadow", "0 24px 70px rgba(39, 76, 119, 0.22)")))
            ]);

    private static VisualPageElement CompactPageHeader(
        string heading,
        string summary) => Element(
            "core.section",
            "Page header",
            layout: Layout("flex", "column", gap: "0.8rem",
                padding: "clamp(2.5rem, 7vw, 5rem) clamp(1rem, 6vw, 6rem)"),
            children:
            [
                Element("core.heading1", "Page heading", heading,
                    styles: Styles(("font-size", "clamp(2.25rem, 6vw, 4.5rem)"),
                        ("margin", "0"))),
                Element("core.paragraph", "Page introduction", summary,
                    styles: Styles(("font-size", "1.125rem"),
                        ("line-height", "1.7"), ("max-width", "64ch"),
                        ("color", "#526070")))
            ]);

    private static VisualPageElement FeatureGrid(
        string heading = "Why this works",
        string summary = "Group the most important benefits into a small number of distinct, scannable ideas.") => Element(
            "core.section",
            "Feature grid",
            layout: Layout("flex", "column", gap: "1.5rem",
                padding: "clamp(3rem, 8vw, 6rem) clamp(1rem, 6vw, 6rem)"),
            children:
            [
                Element("core.heading2", "Feature heading", heading,
                    styles: Styles(("font-size", "clamp(2rem, 4vw, 3rem)"),
                        ("margin", "0"))),
                Element("core.paragraph", "Feature introduction", summary,
                    styles: Styles(("max-width", "62ch"), ("color", "#526070"))),
                Element(
                    "core.grid",
                    "Feature cards",
                    layout: Layout("grid", columns: "repeat(auto-fit, minmax(15rem, 1fr))",
                        gap: "1rem"),
                    children:
                    [
                        FeatureCard(1), FeatureCard(2), FeatureCard(3)
                    ])
            ]);

    private static VisualPageElement FeatureCard(int index) => Element(
        "core.container",
        $"Feature {index}",
        styles: PanelStyles(),
        layout: Layout("flex", "column", gap: "0.75rem", padding: "1.5rem"),
        children:
        [
            BadgeElement($"0{index}"),
            Element("core.heading3", $"Feature {index} heading", $"Distinct benefit {index}"),
            Element("core.paragraph", $"Feature {index} summary",
                "Describe the benefit in terms of a real visitor need or outcome.",
                styles: Styles(("color", "#526070"), ("line-height", "1.6")))
        ]);

    private static VisualPageElement SearchFilterSection() => Element(
        "html.form",
        "Search and filter bar",
        attributes: Attributes(("aria-label", "Search and filter the collection"),
            ("method", "get")),
        styles: PanelStyles(),
        layout: Layout("grid", columns: "minmax(12rem, 2fr) minmax(10rem, 1fr) auto",
            gap: "0.75rem", padding: "1rem",
            alignItems: "end"),
        children:
        [
            Element(
                "core.container",
                "Search field",
                layout: Layout("flex", "column", gap: "0.35rem"),
                children:
                [
                    Element("core.label", "Search label", "Search", Attributes(("for", "directory-search"))),
                    Element("html.input.search", "Search input", attributes: Attributes(
                        ("id", "directory-search"), ("name", "query"),
                        ("placeholder", "Search by keyword"), ("aria-label", "Search")))
                ]),
            Element(
                "core.container",
                "Category field",
                layout: Layout("flex", "column", gap: "0.35rem"),
                children:
                [
                    Element("core.label", "Category label", "Category", Attributes(("for", "directory-category"))),
                    Element("html.select", "Category selector", attributes: Attributes(
                            ("id", "directory-category"), ("name", "category"),
                            ("aria-label", "Category")),
                        children:
                        [
                            Element("core.option", "All categories", "All categories", Attributes(("value", ""))),
                            Element("core.option", "First category", "First category", Attributes(("value", "first")))
                        ])
                ]),
            Element("core.button", "Apply filters", "Apply", styles: PrimaryActionStyles())
        ]);

    private static VisualPageElement DirectoryGrid() => Element(
        "core.section",
        "Directory results",
        attributes: Attributes(("aria-label", "Directory results")),
        layout: Layout("grid", columns: "repeat(auto-fit, minmax(16rem, 1fr))",
            gap: "1rem", padding: "1rem clamp(1rem, 6vw, 6rem) clamp(3rem, 7vw, 6rem)"),
        children:
        [
            ContentCard(1), ContentCard(2), ContentCard(3),
            ContentCard(4), ContentCard(5), ContentCard(6)
        ]);

    private static VisualPageElement ContentCard(int index) => Element(
        "core.container",
        $"Content card {index}",
        styles: PanelStyles(),
        layout: Layout("flex", "column", gap: "0.75rem", padding: "1.35rem"),
        children:
        [
            MetadataElement("Category", "Updated recently"),
            Element("core.heading3", $"Card {index} heading", $"Collection item {index}"),
            Element("core.paragraph", $"Card {index} summary",
                "Add enough context for a visitor to decide whether this item is relevant.",
                styles: Styles(("color", "#526070"), ("line-height", "1.6"))),
            Link("View details")
        ]);

    private static VisualPageElement DetailContent() => Element(
        "core.container",
        "Detail content shell",
        layout: Layout("grid", columns: "minmax(0, 3fr) minmax(14rem, 1fr)",
            gap: "clamp(2rem, 6vw, 5rem)",
            padding: "1rem clamp(1rem, 6vw, 6rem) clamp(4rem, 8vw, 7rem)"),
        children:
        [
            MainContentPanel(
                "A specific, descriptive page title",
                "Open with the essential context and establish what the reader will learn or accomplish.",
                Element("core.heading2", "First section heading", "First important idea"),
                Element("core.paragraph", "First section content",
                    "Develop the idea with concise paragraphs, evidence, examples, and useful links."),
                Element("core.heading2", "Second section heading", "Supporting detail"),
                Element("core.paragraph", "Second section content",
                    "Explain the next part of the subject and make the relationship to the main idea clear.")),
            Element(
                "html.aside",
                "Supporting facts",
                attributes: Attributes(("aria-label", "Supporting information")),
                styles: PanelStyles(),
                layout: Layout("flex", "column", gap: "1rem", padding: "1.25rem"),
                children:
                [
                    Element("core.heading3", "Supporting heading", "At a glance"),
                    MetadataElement("Category", "Value"),
                    MetadataElement("Status", "Current"),
                    MetadataElement("Updated", "Recently"),
                    Link("Related resource")
                ])
        ]);

    private static VisualPageElement SidebarShell(
        string sideHeading,
        VisualPageElement mainContent) => Element(
            "core.container",
            "Sidebar shell",
            layout: Layout("grid", columns: "minmax(13rem, 1fr) minmax(0, 3fr)",
                gap: "clamp(1.5rem, 4vw, 3.5rem)",
                padding: "clamp(2rem, 6vw, 5rem) clamp(1rem, 6vw, 6rem)",
                alignItems: "start"),
            children:
            [
                Element(
                    "html.aside",
                    "Side panel",
                    attributes: Attributes(("aria-label", sideHeading)),
                    styles: PanelStyles(),
                    layout: Layout("flex", "column", gap: "0.75rem", padding: "1.25rem"),
                    children:
                    [
                        Element("core.heading3", "Side panel heading", sideHeading),
                        Link("Overview"), Link("First topic"), Link("Second topic"), Link("Next steps")
                    ]),
                mainContent
            ]);

    private static VisualPageElement MainContentPanel(
        string heading = "Main content heading",
        string summary = "Introduce the purpose of this content area and give the reader the context they need.",
        params VisualPageElement[] additionalChildren) => Element(
            "core.container",
            "Main content box",
            attributes: Attributes(("role", "main")),
            styles: PanelStyles(),
            layout: Layout("flex", "column", gap: "1.15rem",
                padding: "clamp(1.5rem, 5vw, 3.5rem)"),
            children:
            [
                Element("core.heading1", "Main content heading", heading,
                    styles: Styles(("font-size", "clamp(2rem, 5vw, 3.75rem)"),
                        ("margin", "0"))),
                MetadataElement(),
                Element("core.paragraph", "Main content introduction", summary,
                    styles: Styles(("font-size", "1.125rem"),
                        ("line-height", "1.75"), ("color", "#526070"))),
                Element("core.paragraph", "Main content body",
                    "Add clear, well-structured content here. Use descriptive headings and short paragraphs to make the page easy to scan."),
                .. additionalChildren
            ]);

    private static VisualPageElement StatisticsSection() => Element(
        "core.section",
        "Key statistics",
        attributes: Attributes(("aria-label", "Key statistics")),
        layout: Layout("grid", columns: "repeat(auto-fit, minmax(10rem, 1fr))",
            gap: "1rem", padding: "1rem clamp(1rem, 6vw, 6rem)"),
        children:
        [
            Metric("24", "Open items"),
            Metric("8", "Completed today"),
            Metric("96%", "On schedule"),
            Metric("4", "Need attention")
        ]);

    private static VisualPageElement Metric(string value, string label) => Element(
        "core.container",
        $"{label} metric",
        styles: PanelStyles(),
        layout: Layout("flex", "column", gap: "0.25rem", padding: "1.25rem"),
        children:
        [
            Element("core.paragraph", "Metric value", value,
                styles: Styles(("font-size", "2rem"), ("font-weight", "750"),
                    ("margin", "0"))),
            Element("core.paragraph", "Metric label", label,
                styles: Styles(("color", "#526070"), ("margin", "0")))
        ]);

    private static VisualPageElement DashboardPanels() => Element(
        "core.section",
        "Dashboard panels",
        layout: Layout("grid", columns: "repeat(auto-fit, minmax(18rem, 1fr))",
            gap: "1rem", padding: "1rem clamp(1rem, 6vw, 6rem) clamp(4rem, 8vw, 7rem)"),
        children:
        [
            MainContentPanel("Recent activity", "Summarize recent changes, events, or updates."),
            MainContentPanel("Next actions", "List the most useful tasks the user can complete now."),
            MainContentPanel("Health and status", "Explain the current state and identify anything needing attention.")
        ]);

    private static VisualPageElement TestimonialSection() => Element(
        "core.section",
        "Testimonials",
        layout: Layout("grid", columns: "repeat(auto-fit, minmax(18rem, 1fr))",
            gap: "1rem", padding: "clamp(3rem, 8vw, 6rem) clamp(1rem, 6vw, 6rem)"),
        children:
        [
            Testimonial("A concise, authentic statement about the result or experience.", "Customer name · Role"),
            Testimonial("A second approved quotation that supports a different benefit.", "Customer name · Organization")
        ]);

    private static VisualPageElement Testimonial(string quote, string attribution) => Element(
        "core.container",
        "Testimonial",
        styles: PanelStyles(),
        layout: Layout("flex", "column", gap: "1rem", padding: "1.5rem"),
        children:
        [
            Element("core.quote", "Quotation", quote,
                styles: Styles(("font-size", "1.25rem"), ("line-height", "1.6"),
                    ("margin", "0"))),
            Element("core.paragraph", "Attribution", attribution,
                styles: Styles(("color", "#526070"), ("font-weight", "600")))
        ]);

    private static VisualPageElement PricingSection() => Element(
        "core.section",
        "Pricing plans",
        attributes: Attributes(("aria-label", "Pricing plans")),
        layout: Layout("grid", columns: "repeat(auto-fit, minmax(16rem, 1fr))",
            gap: "1rem", padding: "clamp(2rem, 6vw, 5rem) clamp(1rem, 6vw, 6rem)"),
        children:
        [
            PricingCard("Starter", "$19", "For an individual or small project."),
            PricingCard("Professional", "$49", "For a growing team with shared work."),
            PricingCard("Organization", "Let's talk", "For advanced support and governance.")
        ]);

    private static VisualPageElement PricingCard(
        string name,
        string price,
        string summary) => Element(
            "core.container",
            $"{name} plan",
            styles: PanelStyles(),
            layout: Layout("flex", "column", gap: "1rem", padding: "1.5rem"),
            children:
            [
                Element("core.heading3", "Plan name", name),
                Element("core.paragraph", "Plan price", price,
                    styles: Styles(("font-size", "2rem"), ("font-weight", "750"))),
                Element("core.paragraph", "Plan summary", summary,
                    styles: Styles(("color", "#526070"))),
                Element(
                    "core.list",
                    "Included features",
                    children:
                    [
                        Element("core.list-item", "Included feature 1", "Important included feature"),
                        Element("core.list-item", "Included feature 2", "Another included feature"),
                        Element("core.list-item", "Included feature 3", "Support or service level")
                    ]),
                Element("core.button", "Choose plan", "Choose plan", styles: PrimaryActionStyles())
            ]);

    private static VisualPageElement FaqSection() => Element(
        "core.section",
        "Frequently asked questions",
        layout: Layout("flex", "column", gap: "0.75rem",
            padding: "clamp(3rem, 8vw, 6rem) clamp(1rem, 6vw, 6rem)"),
        children:
        [
            Element("core.heading2", "FAQ heading", "Frequently asked questions"),
            Disclosure(1), Disclosure(2), Disclosure(3)
        ]);

    private static VisualPageElement Disclosure(int index) => Element(
        "html.details",
        $"Question {index}",
        styles: PanelStyles(),
        layout: Layout("flex", "column", padding: "1rem"),
        children:
        [
            Element("html.summary", $"Question {index} summary", $"Common question {index}?",
                styles: Styles(("font-weight", "650"), ("cursor", "pointer"))),
            Element("core.paragraph", $"Answer {index}",
                "Answer directly, then add only the context needed to help the reader act.")
        ]);

    private static VisualPageElement CallToActionSection(
        string heading = "Ready to take the next step?",
        string action = "Get started") => Element(
            "core.section",
            "Action banner",
            styles: Styles(("background", "#172a46"), ("color", "#ffffff"),
                ("border-radius", "1.5rem"),
                ("margin", "clamp(2rem, 6vw, 5rem) clamp(1rem, 6vw, 6rem)")),
            layout: Layout("flex", "row", "wrap", gap: "1.5rem",
                padding: "clamp(2rem, 6vw, 4rem)",
                alignItems: "center", justifyContent: "space-between"),
            children:
            [
                Element(
                    "core.container",
                    "Action copy",
                    layout: Layout("flex", "column", gap: "0.5rem"),
                    children:
                    [
                        Element("core.heading2", "Action heading", heading),
                        Element("core.paragraph", "Action summary",
                            "Give the visitor one clear reason to continue now.",
                            styles: Styles(("color", "#d9e5f5")))
                    ]),
                Element("core.button", "Primary action", action,
                    styles: Styles(("background", "#ffffff"), ("color", "#172a46"),
                        ("border", "0"), ("border-radius", "0.7rem"),
                        ("padding", "0.85rem 1.2rem"), ("font-weight", "700")))
            ]);

    private static VisualPageElement FooterSection() => Element(
        "core.container",
        "Site footer",
        attributes: Attributes(("role", "contentinfo"), ("aria-label", "Site footer")),
        styles: Styles(("background", "#101827"), ("color", "#edf3fb")),
        layout: Layout("flex", "row", "wrap", gap: "1rem",
            padding: "2rem clamp(1rem, 6vw, 6rem)",
            alignItems: "center", justifyContent: "space-between"),
        children:
        [
            Element("core.paragraph", "Copyright", "© Site name. Add the correct year and rights statement."),
            Element(
                "core.nav",
                "Footer navigation",
                attributes: Attributes(("aria-label", "Footer navigation")),
                layout: Layout("flex", "row", "wrap", gap: "1rem"),
                children: [Link("Privacy"), Link("Terms"), Link("Contact")])
        ]);

    private static VisualPageElement ContactSection() => Element(
        "core.section",
        "Contact options",
        layout: Layout("grid", columns: "minmax(14rem, 1fr) minmax(0, 2fr)",
            gap: "clamp(2rem, 6vw, 5rem)",
            padding: "1rem clamp(1rem, 6vw, 6rem) clamp(4rem, 8vw, 7rem)"),
        children:
        [
            Element(
                "core.container",
                "Contact details",
                styles: PanelStyles(),
                layout: Layout("flex", "column", gap: "1rem", padding: "1.5rem"),
                children:
                [
                    Element("core.heading2", "Contact details heading", "Contact details"),
                    Element("core.paragraph", "Response time", "Typical response: one to two business days."),
                    Link("hello@example.com"),
                    Element("core.paragraph", "Office hours", "Monday–Friday · 9:00–17:00")
                ]),
            Element(
                "html.form",
                "Inquiry form",
                attributes: Attributes(("aria-label", "Inquiry form"), ("method", "post")),
                styles: PanelStyles(),
                layout: Layout("flex", "column", gap: "0.75rem", padding: "1.5rem"),
                children:
                [
                    Element("core.heading2", "Form heading", "Tell us about your project"),
                    FormField("Name", "contact-name", "html.input.text", "name"),
                    FormField("Email", "contact-email", "html.input.email", "email"),
                    Element("core.label", "Message label", "Message", Attributes(("for", "contact-message"))),
                    Element("html.textarea", "Message input", attributes: Attributes(
                        ("id", "contact-message"), ("name", "message"),
                        ("rows", "6"), ("required", "true"), ("aria-label", "Message"))),
                    Element("core.button", "Submit inquiry", "Send inquiry",
                        Attributes(("type", "submit")), PrimaryActionStyles())
                ])
        ]);

    private static VisualPageElement FormField(
        string label,
        string id,
        string descriptorId,
        string autocomplete) => Element(
            "core.container",
            $"{label} field",
            layout: Layout("flex", "column", gap: "0.35rem"),
            children:
            [
                Element("core.label", $"{label} label", label, Attributes(("for", id))),
                Element(descriptorId, $"{label} input", attributes: Attributes(
                    ("id", id), ("name", label.ToLowerInvariant()),
                    ("autocomplete", autocomplete), ("required", "true"),
                    ("aria-label", label)))
            ]);

    private static VisualPageElement PortfolioGrid() => Element(
        "core.section",
        "Project gallery",
        attributes: Attributes(("aria-label", "Selected projects")),
        layout: Layout("grid", columns: "repeat(auto-fit, minmax(18rem, 1fr))",
            gap: "1rem", padding: "clamp(2rem, 6vw, 5rem) clamp(1rem, 6vw, 6rem)"),
        children:
        [
            PortfolioCard(1), PortfolioCard(2), PortfolioCard(3), PortfolioCard(4)
        ]);

    private static VisualPageElement PortfolioCard(int index) => Element(
        "core.container",
        $"Project {index}",
        styles: PanelStyles(),
        layout: Layout("flex", "column", gap: "0.8rem", padding: "1rem"),
        children:
        [
            Element(
                "core.container",
                $"Project {index} image placeholder",
                attributes: Attributes(("role", "img"),
                    ("aria-label", $"Replace with an image for project {index}")),
                styles: Styles(("min-height", "15rem"), ("border-radius", "0.8rem"),
                    ("background", "linear-gradient(145deg, #d9e5f5, #a9bfdc)"))),
            MetadataElement("Project type", "Year"),
            Element("core.heading3", $"Project {index} heading", $"Selected project {index}"),
            Element("core.paragraph", $"Project {index} summary",
                "Describe the challenge, contribution, and most meaningful result."),
            Link("View project")
        ]);

    private static VisualPageElement AlertElement() => Element(
        "core.container",
        "Information callout",
        attributes: Attributes(("role", "status"), ("aria-label", "Important information")),
        styles: Styles(("background", "#eef6ff"), ("border", "1px solid #b8d7fa"),
            ("border-left", "4px solid #2876c7"), ("border-radius", "0.75rem")),
        layout: Layout("flex", "column", gap: "0.4rem", padding: "1rem"),
        children:
        [
            Element("core.heading3", "Callout heading", "Important information"),
            Element("core.paragraph", "Callout message",
                "Add concise guidance, status, or context that should not be missed.")
        ]);

    private static VisualPageElement ButtonGroup() => Element(
        "core.container",
        "Action group",
        attributes: Attributes(("role", "group"), ("aria-label", "Page actions")),
        layout: Layout("flex", "row", "wrap", gap: "0.75rem", alignItems: "center"),
        children:
        [
            Element("core.button", "Primary action", "Primary action", styles: PrimaryActionStyles()),
            Element("core.link", "Secondary action", "Learn more",
                Attributes(("href", "#")),
                Styles(("color", "#274c77"), ("font-weight", "650")))
        ]);

    private static VisualPageElement BadgeElement(string text = "Status") => Element(
        "core.inline",
        "Status badge",
        text,
        styles: Styles(("display", "inline-block"), ("width", "fit-content"),
            ("padding", "0.3rem 0.65rem"), ("border-radius", "999px"),
            ("background", "#dbeafe"), ("color", "#174f8a"),
            ("font-size", "0.8rem"), ("font-weight", "700")));

    private static VisualPageElement PaginationElement() => Element(
        "core.nav",
        "Pagination",
        attributes: Attributes(("aria-label", "Pagination")),
        layout: Layout("flex", "row", "wrap", gap: "0.6rem",
            padding: "1rem clamp(1rem, 6vw, 6rem) clamp(3rem, 7vw, 5rem)",
            alignItems: "center", justifyContent: "center"),
        children:
        [
            Link("Previous"), Link("1"), Link("2"), Link("3"), Link("Next")
        ]);

    private static VisualPageElement BreadcrumbElement() => Element(
        "core.nav",
        "Breadcrumb trail",
        attributes: Attributes(("aria-label", "Breadcrumb")),
        layout: Layout("flex", "row", "wrap", gap: "0.5rem",
            padding: "1.25rem clamp(1rem, 6vw, 6rem)"),
        children:
        [
            Link("Home"), Element("core.inline", "Separator 1", "/"),
            Link("Parent"), Element("core.inline", "Separator 2", "/"),
            Element("core.inline", "Current page", "Current page")
        ]);

    private static VisualPageElement MetadataElement(
        string first = "Category",
        string second = "Updated recently") => Element(
            "core.container",
            "Content metadata",
            layout: Layout("flex", "row", "wrap", gap: "0.75rem", alignItems: "center"),
            children:
            [
                Element("core.inline", "First metadata item", first,
                    styles: Styles(("color", "#526070"), ("font-size", "0.875rem"))),
                Element("core.inline", "Metadata separator", "·",
                    styles: Styles(("color", "#8a96a5"))),
                Element("core.inline", "Second metadata item", second,
                    styles: Styles(("color", "#526070"), ("font-size", "0.875rem")))
            ]);

    private static VisualPageElement ProfileElement() => Element(
        "core.container",
        "Profile summary",
        styles: PanelStyles(),
        layout: Layout("grid", columns: "5rem minmax(0, 1fr)", gap: "1rem",
            padding: "1.25rem", alignItems: "center"),
        children:
        [
            Element(
                "core.container",
                "Avatar placeholder",
                attributes: Attributes(("role", "img"),
                    ("aria-label", "Replace with a profile image")),
                styles: Styles(("width", "5rem"), ("height", "5rem"),
                    ("border-radius", "50%"), ("background", "#c9d8ea"))),
            Element(
                "core.container",
                "Profile details",
                layout: Layout("flex", "column", gap: "0.35rem"),
                children:
                [
                    Element("core.heading3", "Profile name", "Person name"),
                    Element("core.paragraph", "Profile role", "Role or specialty",
                        styles: Styles(("color", "#526070"), ("font-weight", "600"))),
                    Element("core.paragraph", "Profile biography",
                        "Add a short biography focused on relevant experience or contribution."),
                    Link("View profile")
                ])
        ]);

    private static VisualPageElement Link(string text) => Element(
        "core.link",
        $"{text} link",
        text,
        Attributes(("href", "#")),
        Styles(("color", "#235f9d"), ("font-weight", "600")));

    private static VisualPageElement Element(
        string descriptorId,
        string name,
        string text = "",
        Dictionary<string, string>? attributes = null,
        Dictionary<string, string>? styles = null,
        VisualPageLayoutSpec? layout = null,
        IEnumerable<VisualPageElement>? children = null) => new()
        {
            DescriptorId = descriptorId,
            Name = name,
            Text = text,
            Attributes = attributes ?? new Dictionary<string, string>(StringComparer.Ordinal),
            Styles = styles ?? new Dictionary<string, string>(StringComparer.Ordinal),
            Layout = layout is null
                ? []
                : new Dictionary<VisualPageBreakpoint, VisualPageLayoutSpec>
                {
                    [VisualPageBreakpoint.Base] = layout
                },
            Children = children?.ToList() ?? []
        };

    private static VisualPageLayoutSpec Layout(
        string display,
        string direction = "",
        string wrap = "",
        string columns = "",
        string gap = "",
        string padding = "",
        string alignItems = "",
        string justifyContent = "") => new()
        {
            Display = display,
            FlexDirection = direction,
            FlexWrap = wrap,
            GridTemplateColumns = columns,
            Gap = gap,
            Padding = padding,
            AlignItems = alignItems,
            JustifyContent = justifyContent
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

    private static Dictionary<string, string> PanelStyles() => Styles(
        ("background", "#ffffff"),
        ("border", "1px solid #dce2ea"),
        ("border-radius", "1rem"),
        ("box-shadow", "0 12px 35px rgba(23, 42, 70, 0.07)"));

    private static Dictionary<string, string> PrimaryActionStyles() => Styles(
        ("background", "#235f9d"),
        ("color", "#ffffff"),
        ("border", "0"),
        ("border-radius", "0.7rem"),
        ("padding", "0.85rem 1.2rem"),
        ("font-weight", "700"));
}
