using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using System.IO;

internal static class StudioTabCloseSemanticsVerifier
{
    public static async Task VerifyAsync(string outputRoot)
    {
        var root = Path.Combine(outputRoot, "tab-close-semantics");
        var projectRoot = Path.Combine(root, "project");
        Directory.CreateDirectory(root);
        const string draft = "/* Keep this CSS draft after its tab closes. */";
        using (var tab = await CreateTabAsync(root, projectRoot, create: true))
        {
            tab.Project.LayoutOverride = null;
            tab.RestoreLayoutPreferences(new StudioLayoutSnapshot
            {
                Root = new StudioLayoutNode
                {
                    NodeId = "outer-split", Kind = StudioLayoutNodeKind.Split,
                    First = new StudioLayoutNode
                    {
                        NodeId = "nested-split", Kind = StudioLayoutNodeKind.Split,
                        First = Pane("canvas-pane", "canvas"), Second = Pane("css-pane", "css")
                    },
                    Second = Pane("generated-pane", "generated-razor")
                }
            }, []);
            tab.UserCss += "\n" + draft;
            tab.SelectPaneContent("css-pane", "css");
            var originalRevision = tab.Project.Revision;
            Require(!tab.ClosePaneContent("not-a-pane", "css") && tab.Project.Revision == originalRevision,
                "A stale close target must leave the current layout and project revision untouched.");
            Require(tab.ClosePaneContent("css-pane", "css")
                && Contents(tab.Layout.Root).ToHashSet().SetEquals(["canvas", "generated-razor"])
                && tab.Layout.Root.Kind == StudioLayoutNodeKind.Split,
                "Closing a tab in a nested first branch must preserve its neighboring pane and the outer second branch.");
            Require(tab.UserCss.Contains(draft, StringComparison.Ordinal)
                && tab.CaptureUserSourceRecovery().Any(recovery => recovery.Content.Contains(draft, StringComparison.Ordinal)),
                "Closing the built-in CSS tab must retain its dirty buffer and recoverable draft.");
            Require(tab.ClosePaneContent("generated-pane", "generated-razor")
                && tab.ClosePaneContent("canvas-pane", "canvas") && IsEmpty(tab.Layout.Root),
                "Closing the last generated and Canvas tabs must leave an explicit empty pane.");
            Require(await tab.SaveProjectAsync(), "The project and hidden source draft must save with an empty tab layout.");
        }

        using var reopened = await CreateTabAsync(root, projectRoot, create: false);
        Require(IsEmpty(reopened.Layout.Root), "Reopening the saved project must preserve its empty layout without resurrecting Canvas.");
        Require(reopened.UserCss.Contains(draft, StringComparison.Ordinal), "Saving the closed source tab must preserve its exact draft text on disk.");
        reopened.RequestPaneContent("css");
        Require(reopened.Layout.Root.Kind == StudioLayoutNodeKind.Pane && Contents(reopened.Layout.Root).SequenceEqual(["css"]),
            "Reopening a source tab in an empty workspace must not reopen Canvas implicitly.");
        reopened.RequestPaneContent("generated-razor");
        var beforeCanvas = Contents(reopened.Layout.Root).ToHashSet();
        reopened.RequestPaneContent("canvas");
        Require(Contents(reopened.Layout.Root).ToHashSet().SetEquals(beforeCanvas.Append("canvas")),
            "Explicitly reopening Canvas must retain existing source and generated tabs.");
        Console.WriteLine("STUDIO_TAB_CLOSE_OK|nested-sibling-retention|stale-target|source-recovery|empty-save-reload|reopen-source|reopen-canvas");
    }

    private static StudioLayoutNode Pane(string id, string content) => new()
    { NodeId = id, Kind = StudioLayoutNodeKind.Pane, PaneContents = [content], ActiveContentId = content };
    private static bool IsEmpty(StudioLayoutNode node) => node.Kind == StudioLayoutNodeKind.Pane
        && node.PaneContents.Count == 0 && node.ActiveContentId.Length == 0;
    private static IEnumerable<string> Contents(StudioLayoutNode node)
    {
        foreach (var content in node.PaneContents) yield return content;
        if (node.First is not null) foreach (var content in Contents(node.First)) yield return content;
        if (node.Second is not null) foreach (var content in Contents(node.Second)) yield return content;
    }
    private static async Task<VisualPageStudioTabViewModel> CreateTabAsync(string root, string projectRoot, bool create)
    {
        var catalog = new VisualPageComponentCatalog();
        var validator = new VisualPageSchemaValidator(catalog);
        var generator = new VisualPageGenerator(catalog, validator);
        var projects = new VisualPageProjectService(validator, generator, catalog);
        var session = create ? await projects.CreateAsync(projectRoot, "Tab close fixture", "Tab.Close.Fixture")
            : await projects.OpenAsync(projectRoot);
        return await VisualPageStudioTabViewModel.CreateAsync(session, projects, new VisualPageMutationEngine(validator), generator,
            validator, catalog, new VisualPageTemplateService(catalog, Path.Combine(root, "personal-templates")),
            new VisualPageDesignCanvasRenderer(catalog),
            new VisualPagePreviewBuildService(new BoundedProcessRunner(), generator, Path.Combine(root, "preview-cache")),
            new VisualPageLocalHostService(new BoundedProcessRunner()), new VisualPageUserSourceService(),
            new AtlasArchivePublicationService(new BoundedProcessRunner(), projects, generator, validator, Path.Combine(root, "publication")));
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
