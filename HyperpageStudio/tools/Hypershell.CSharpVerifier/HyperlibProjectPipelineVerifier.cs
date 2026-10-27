using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;

/// <summary>Exercises real toolkit imports, empty staging, independent Pages, live layout propagation, and conflict protection.</summary>
public static class HyperlibProjectPipelineVerifier
{
    public static async Task<string> RunAsync(string outputDirectory, string sourceLibraryRoot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var protectedFiles = new[] { "Content/library.json", "CoreLibrary/DesignGroups/blank/Project/authoring.template.json", "hyperlib.app.json" }
            .Select(relative => Path.Combine(sourceLibraryRoot, relative)).Where(File.Exists).ToDictionary(path => path, Hash);
        var generator = new HyperlibWorkProjectService(sourceLibraryRoot);
        var catalogue = generator.GetCatalog();
        Require(catalogue.Count > 100 && catalogue.Select(item => item.GroupId).Distinct().Count() > 10,
            "Shopping must expose the real expanded Core Template Library, not only a fixed starter menu.");
        var header = catalogue.First(item => item.GroupId == "blank" && item.ComponentName == "HeaderBlock" && item.Kind == "element");
        var body = catalogue.First(item => item.GroupId == "blank" && item.ComponentName == "RichTextBlock" && item.Kind == "element");
        var footer = catalogue.First(item => item.GroupId == "blank" && item.ComponentName == "FooterBlock" && item.Kind == "element");
        var image = catalogue.First(item => item.GroupId == "blank" && item.ComponentName == "ImageBlock" && item.Kind == "element");
        var special = catalogue.First(item => item.GroupId == "blank" && item.ComponentName == "CounterBlock" && item.Kind == "element");
        var added = catalogue.First(item => item.GroupId == "blank" && item.ComponentName == "CalloutBlock" && item.Kind == "element");
        var layout = catalogue.First(item => item.GroupId == "documentation-desk" && item.Kind == "layout");
        var twoColumns = catalogue.Single(item => item.Id == "blank:layout-two-columns");
        var threeColumns = catalogue.Single(item => item.Id == "blank:layout-three-columns");
        var definition = new HyperlibWorkProjectDefinition { PipelineVersion = 2, WorkTitle = "A staged Work", Description = "",
            SelectedToolsetIds = [header.Id, body.Id, footer.Id, image.Id, special.Id, layout.Id, twoColumns.Id, threeColumns.Id],
            LandingTemplate = new() { HeaderTemplateId = header.Id, BodyTemplateId = body.Id, FooterTemplateId = footer.Id,
                BackgroundColor = "#f8f5ef", AccentColor = "#2c765a", ContentAlignment = "center", NavigationPlacement = "bottom" },
            PageTemplate = new() { HeaderTemplateId = header.Id, BodyTemplateId = body.Id, FooterTemplateId = footer.Id,
                BackgroundColor = "#14212a", AccentColor = "#65a88e", NavigationPlacement = "left", RepeatingObjects = [new() { TemplateObjectId = image.Id, Region = "main", Order = 0 }] } };
        var parent = Path.Combine(outputDirectory, "pipeline-" + Guid.NewGuid().ToString("N"));
        var project = await generator.CreateAsync(parent, "work", definition, ct);
        var libraryRoot = Path.GetFullPath(Path.Combine(project, "../.."));
        var pipelineService = new HyperlibProjectPipelineService();
        var pipeline = await pipelineService.ReadAsync(project, ct) ?? throw new InvalidOperationException("Pipeline configuration missing.");
        Require(pipeline.SelectedToolsetIds.SequenceEqual(definition.SelectedToolsetIds) && pipeline.Toolkit.All(item => item.BlockTypeIds.All(type =>
            TemplateObjectLibrary.ReadDefinition(project).Blocks.Any(block => block.Id == type))), "Toolkit IDs must map to imported compiled object types.");
        Require(pipeline.SourceLibraryRoot == Path.GetFullPath(sourceLibraryRoot), "The original collection root must be retained as read-only provenance.");
        var template = TemplateObjectLibrary.ReadDefinition(project);
        var stagedTwo = HyperlibProjectPipelineService.Compose(new() { LayoutTemplateId = twoColumns.Id, IncludeNavigation = false }, pipeline.Toolkit, template, "page");
        var stagedThree = HyperlibProjectPipelineService.Compose(new() { LayoutTemplateId = threeColumns.Id, IncludeNavigation = false }, pipeline.Toolkit, template, "page");
        var laidOutContent = ContentJson.Clone(stagedTwo.Blocks);
        laidOutContent[1].Values["text"] = ContentJson.Value("Text inside a shared column.");
        laidOutContent[2].Values["span"] = ContentJson.Value("full");
        var propagatedColumns = HyperlibProjectPipelineService.MergeLayout(laidOutContent, stagedTwo.Blocks, stagedThree.Blocks, template);
        Require(propagatedColumns.Single(block => block.Id == laidOutContent[1].Id).Values["span"].GetString() == "third"
            && propagatedColumns.Single(block => block.Id == laidOutContent[1].Id).Values["text"].GetString() == "Text inside a shared column."
            && propagatedColumns.Single(block => block.Id == laidOutContent[2].Id).Values["span"].GetString() == "full",
            "Shared column changes must update untouched structural defaults while preserving real text and explicit per-Page overrides.");
        var store = new ContentStore(libraryRoot) { TemplateResolver = package => package == pipeline.WorkId ? TemplateObjectLibrary.ReadDefinition(project) : null };
        var state = await store.ReadAsync(ct);
        Require(state.Pages.Count == 1 && state.Pages.ContainsKey(pipeline.LandingPageId), "Creation must produce zero content Pages and only an empty Work landing scaffold.");
        Require(state.Pages.Values.SelectMany(page => page.Blocks).All(block => template.Blocks.Single(schema => schema.Id == block.Type).Fields
            .Where(field => field.Type is "string" or "text" or "media" or "url").All(field => !block.Values.TryGetValue(field.Id, out var value)
                || value.ValueKind == JsonValueKind.String && value.GetString() == "")), "Staging must not write finished text, logos, images, or example prose.");
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Create content when the author is ready", Operations =
            [new() { Kind = "createNode", Id = "first-page", NodeType = "page", ParentId = pipeline.WorkId, Title = "First authored Page" },
             new() { Kind = "createNode", Id = "second-page", NodeType = "page", ParentId = pipeline.WorkId, Title = "Second authored Page" }] }, ct);
        Require(state.Pages["first-page"].Blocks.Select(block => block.Id).SequenceEqual(pipeline.PageTemplate.Blocks.Select(block => block.Id)),
            "A Page must independently clone the shared staged layout.");
        var first = ContentJson.Clone(state.Pages["first-page"]);
        var bodySlot = first.Blocks.First(block => template.Blocks.Single(schema => schema.Id == block.Type).Fields.Any(field => field.Id == "text"));
        bodySlot.Values["text"] = ContentJson.Value("Authored content must survive shared-layout changes.");
        var imageSlot = first.Blocks.First(block => template.Blocks.Single(schema => schema.Id == block.Type).Fields.Any(field => field.Id == "src"));
        imageSlot.Values["src"] = ContentJson.Value("/media/my-owned-image.png");
        var independentType = pipeline.Toolkit.Single(item => item.ChoiceId == special.Id).BlockTypeIds.Single();
        first.Blocks.Add(new() { Id = "independent-special", Type = independentType, Region = "main", Values = [] });
        first.Sources.Add(new() { Id = "source-one", Title = "Author source", Url = "https://example.com/source", RetrievedAt = DateTimeOffset.UtcNow,
            BlockIds = [imageSlot.Id, bodySlot.Id] });
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Write real content", Operations = [new() { Kind = "savePage", Page = first }] }, ct);
        var staged = ContentJson.Clone(pipeline.PageTemplate.Definition);
        staged.ContentAlignment = "right";
        staged.NavigationPlacement = "right";
        staged.BackgroundColor = "#f8f5ef";
        staged.RepeatingObjects.Clear();
        pipeline = await pipelineService.UpdateTemplatesAsync(project, new() { ExpectedPipelineRevision = pipeline.Revision, ExpectedContentRevision = state.Revision,
            LandingTemplate = pipeline.LandingTemplate.Definition, PageTemplate = staged }, ct);
        state = await store.ReadAsync(ct);
        var reopenedFirst = state.Pages["first-page"];
        Require(reopenedFirst.Blocks.Single(block => block.Id == bodySlot.Id).Values["text"].GetString() == "Authored content must survive shared-layout changes.",
            "Existing text and stable slot identity must survive propagation.");
        Require(reopenedFirst.Blocks.Single(block => block.Id == imageSlot.Id).Values["src"].GetString() == "/media/my-owned-image.png",
            "A populated image removed from the shared template must remain independent content with its original stable ID and media URL.");
        Require(reopenedFirst.Blocks.Any(block => block.Id == "independent-special") && reopenedFirst.Sources.Single().BlockIds.SequenceEqual(new[] { imageSlot.Id, bodySlot.Id }),
            "Independent objects and citation links must stay intact.");
        Require(!state.Pages["second-page"].Blocks.Any(block => block.Id == imageSlot.Id)
            && state.Nodes.Where(node => node.Id is "first-page" or "second-page").All(node => node.Appearance["studioAlignment"].GetString() == "right"),
            "Shared layout propagation updates every content Page and removes only untouched empty slots.");
        Require(state.Pages[pipeline.LandingPageId].Blocks.Select(block => block.Region).Contains("footer"), "The independent Work landing template must remain separate.");
        var stablePipeline = await File.ReadAllTextAsync(Path.Combine(project, ".studio", "pipeline.json"), ct);
        var stableTemplate = await File.ReadAllTextAsync(Path.Combine(project, "authoring.template.json"), ct);
        var stableRevision = state.Revision;
        try
        {
            await pipelineService.UpdateTemplatesAsync(project, new() { ExpectedPipelineRevision = pipeline.Revision,
                ExpectedContentRevision = state.Revision - 1, LandingTemplate = pipeline.LandingTemplate.Definition, PageTemplate = staged }, ct);
            throw new InvalidOperationException("A stale content revision must be rejected.");
        }
        catch (ConflictException) { }
        Require(await File.ReadAllTextAsync(Path.Combine(project, ".studio", "pipeline.json"), ct) == stablePipeline
            && await File.ReadAllTextAsync(Path.Combine(project, "authoring.template.json"), ct) == stableTemplate
            && (await store.ReadAsync(ct)).Revision == stableRevision, "Stale template changes must have no partial source or content effects.");
        var invalid = ContentJson.Clone(staged);
        invalid.HeaderTemplateId = "blank:not-selected";
        try
        {
            await pipelineService.UpdateTemplatesAsync(project, new() { ExpectedPipelineRevision = pipeline.Revision,
                ExpectedContentRevision = state.Revision, LandingTemplate = pipeline.LandingTemplate.Definition, PageTemplate = invalid }, ct);
            throw new InvalidOperationException("Unselected objects must not silently expand the toolkit.");
        }
        catch (InvalidDataException) { }
        var foundation = (await pipelineService.ReadDefinitionAsync(project, ct))!;
        foundation.SelectedToolsetIds.Remove(special.Id);
        foundation.SelectedToolsetIds.Add(added.Id);
        foundation.PageTemplate.RepeatingObjects.Add(new() { TemplateObjectId = added.Id, Region = "footer", Order = 0 });
        pipeline = await generator.UpdateFoundationAsync(project, foundation, pipeline.Revision, state.Revision, ct);
        state = await store.ReadAsync(ct);
        Require(pipeline.Toolkit.Any(item => item.ChoiceId == added.Id) && !pipeline.Toolkit.Any(item => item.ChoiceId == special.Id),
            "Returning to shopping must add real objects and remove palette selections.");
        Require(state.Pages["first-page"].Blocks.Any(block => block.Id == "independent-special")
            && TemplateObjectLibrary.ReadDefinition(project).Blocks.Any(block => block.Id == independentType),
            "Removing an object from the toolkit must retain compiled schema, source, and existing authored objects.");
        Require(state.Pages.Values.Where(page => page.Id != pipeline.LandingPageId).All(page => page.Blocks.Any(block =>
            pipeline.Toolkit.Single(item => item.ChoiceId == added.Id).BlockTypeIds.Contains(block.Type))), "A newly staged object must propagate to existing content Pages.");
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Create later Page from current shared layout", Operations =
            [new() { Kind = "createNode", Id = "third-page", NodeType = "page", ParentId = pipeline.WorkId, Title = "Added later" }] }, ct);
        Require(state.Pages["third-page"].Blocks.Select(block => block.Id).SequenceEqual(pipeline.PageTemplate.Blocks.Select(block => block.Id)),
            "Pages added after a foundation change must independently clone its newest recipe.");
        var portable = (await pipelineService.ReadDefinitionAsync(project, ct))!;
        portable.SelectedToolsetIds.Remove(layout.Id);
        portable.PageTemplate.ContentAlignment = "center";
        var missingCollection = Path.Combine(outputDirectory, "missing-original-collection", Guid.NewGuid().ToString("N"));
        pipeline = await new HyperlibWorkProjectService(missingCollection).UpdateFoundationAsync(project, portable, pipeline.Revision, state.Revision, ct);
        state = await store.ReadAsync(ct);
        Require(pipeline.PageTemplate.Definition.ContentAlignment == "center" && !pipeline.SelectedToolsetIds.Contains(layout.Id)
            && pipeline.SourceLibraryRoot == Path.GetFullPath(sourceLibraryRoot)
            && state.Pages["first-page"].Blocks.Any(block => block.Id == "independent-special"),
            "A moved project must restage and narrow its owned toolkit without the original collection or loss of source provenance/content.");
        foreach (var protectedFile in protectedFiles) Require(Hash(protectedFile.Key) == protectedFile.Value, "Generation must not mutate the Core Template Library.");
        Require(File.Exists(Path.Combine(libraryRoot, "src", "HyperLib.Host", "StudioPipeline", "HyperlibProjectPipelineService.cs"))
            && File.Exists(Path.Combine(libraryRoot, "src", "HyperLib.Host", "wwwroot", "studio-authoring", "index.html")), "The private gateway must carry its own portable Studio authoring tools and pipeline implementation.");
        Require(File.Exists(Path.Combine(project, "Templates", "StudioRuntime", "HyperLib.Runtime", "Components", "DynamicPage.razor"))
            && File.ReadAllText(Path.Combine(project, "src", "Pages", "Pages.csproj")).Contains("Templates/StudioRuntime/HyperLib.Runtime", StringComparison.Ordinal),
            "The Work's portable renderer must reference its owned staged-layout SDK, captured by existing reader supervisors.");
        await HyperlibGuidedTemplateVerifier.VerifyAsync(project, sourceLibraryRoot, outputDirectory, ct);
        await BuildAsync(Path.Combine(project, "src", "Host", "Host.csproj"), Path.Combine(outputDirectory, "pipeline-work-build.log"), ct);
        await BuildAsync(Path.Combine(libraryRoot, "src", "HyperLib.Host", "HyperLib.Host.csproj"), Path.Combine(outputDirectory, "pipeline-gateway-build.log"), ct);
        var result = "HYPERLIB_PROJECT_PIPELINE_OK real-expanded-catalog|selected-toolkit|empty-staging|two-reusable-layouts|no-content-page-count|independent-page-clones|live-propagation|authored-text-preserved|stable-media-object-preserved|citations-preserved|independent-objects-preserved|empty-slot-removal|optimistic-conflict-no-effects|revisit-shopping|palette-removal-preserves-existing-objects|added-objects-propagate|future-pages-current-recipe|source-library-preserved|portable-authoring-host|owned-renderer-sdk|work-build|gateway-build";
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "hyperlib-project-pipeline-result.txt"), result + Environment.NewLine + project, ct);
        return result;
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static async Task BuildAsync(string project, string log, CancellationToken ct)
    {
        var start = new System.Diagnostics.ProcessStartInfo("dotnet") { WorkingDirectory = Path.GetDirectoryName(project)!, UseShellExecute = false,
            CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "build", project, "-c", "Release", "--nologo", "-v:minimal" }) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("The generated project build could not start.");
        var standard = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        try { await process.WaitForExitAsync(ct); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        var output = await standard + Environment.NewLine + await error;
        await File.WriteAllTextAsync(log, output, ct);
        Require(process.ExitCode == 0, "The generated portable project must build. " + output);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
