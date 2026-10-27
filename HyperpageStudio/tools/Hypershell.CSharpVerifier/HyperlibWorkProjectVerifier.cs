using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;

/// <summary>Exercises actual catalogue choices, durable content, independent builds, and the real loopback gateway.</summary>
public static class HyperlibWorkProjectVerifier
{
    public static async Task<string> RunAsync(string outputDirectory, string sourceLibraryRoot, CancellationToken ct = default)
    {
        var output = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        var sourceMarkers = new[] { "Content/library.json", "hyperlib.app.json", "CoreLibrary/DesignGroups/blank/Project/authoring.template.json" }
            .Select(relative => Path.Combine(sourceLibraryRoot, relative)).Where(File.Exists).ToDictionary(path => path, Hash, StringComparer.OrdinalIgnoreCase);
        var service = new HyperlibWorkProjectService(sourceLibraryRoot);
        var catalog = service.GetCatalog();
        Require(catalog.Any(choice => choice.Kind == "layout") && catalog.Any(choice => choice.ComponentName == "HeaderBlock") && catalog.Any(choice => choice.ComponentName == "ImageBlock"), "The wizard must expose real layouts, headers, and media objects.");
        var firstLayout = catalog.First(choice => choice.GroupId == "documentation-desk" && choice.Kind == "layout");
        var secondLayout = catalog.First(choice => choice.GroupId == "gallery-wall" && choice.Kind == "layout");
        var header = catalog.First(choice => choice.GroupId == "blank" && choice.ComponentName == "HeaderBlock");
        var body = catalog.First(choice => choice.GroupId == "blank" && choice.ComponentName == "RichTextBlock");
        var image = catalog.First(choice => choice.GroupId == "blank" && choice.ComponentName == "ImageBlock");
        var special = catalog.First(choice => choice.GroupId == "blank" && choice.ComponentName == "CounterBlock");
        var localImage = Path.Combine(output, "wizard-image.png");
        // A valid 1x1 PNG verifies that the user's asset is copied rather than linked into the generated project.
        await File.WriteAllBytesAsync(localImage, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aOCkAAAAASUVORK5CYII="), ct);
        var definition = new HyperlibWorkProjectDefinition
        {
            WorkTitle = "A custom research Work", Description = "An independent two-Page starter.", TemplateId = "documentation-desk",
            LandingPage = new() { Title = "Welcome", Description = "Start exploring this Work.", HeaderChoiceId = header.Id, BodyChoiceId = body.Id, Background = "#eef1f4", Accent = "#36617c" },
            PageDefinitions =
            [
                new() { Title = "Field notes", Description = "A focused first Page.", LayoutChoiceId = firstLayout.Id, HeaderChoiceId = header.Id, BodyChoiceId = body.Id,
                    ImageChoiceId = image.Id, Images = [new() { Path = localImage, Alt = "A white pixel used as an import test", Caption = "Imported local image" }],
                    Background = "#fff8e9", Accent = "#af5636", SpecialChoiceIds = [special.Id] },
                new() { Title = "Image study", Description = "A different second Page.", LayoutChoiceId = secondLayout.Id, Background = "#17122d", Accent = "#f2b84b", BackgroundImagePath = localImage, IncludeNavigation = false }
            ]
        };
        var parent = Path.Combine(output, "new-parent", Guid.NewGuid().ToString("N"));
        var project = await service.CreateAsync(parent, "custom-work", definition, ct);
        var library = Path.GetFullPath(Path.Combine(project, "../.."));
        Require(HyperlibWorkPreviewHostService.IsGeneratedLibrary(library), "The independent library marker must identify this generated project.");
        using var workspace = new HyperlibStudioWorkspaceService();
        var snapshot = await workspace.ReadAsync(project, ct);
        Require(snapshot.Context.HasLibraryContent && snapshot.Context.LibraryRoot == library && snapshot.Nodes.Count(node => node.Type == "page") == 3,
            "Studio must recognize the actual Work and its landing plus two content Pages.");
        var state = snapshot.State!;
        var workId = snapshot.Context.WorkId!;
        var template = TemplateObjectLibrary.ReadDefinition(project);
        ContentRules.Validate(state, package => package == workId ? template : null);
        var first = state.Nodes.Single(node => node.Title == "Field notes");
        var second = state.Nodes.Single(node => node.Title == "Image study");
        Require(first.Appearance["background"].GetString() == "#fff8e9" && second.Appearance["background"].GetString() == "#17122d", "Per-Page appearance must be retained independently.");
        Require(second.Appearance["ink"].GetString() == "#f2f5f4" && first.Appearance["ink"].GetString() == "#202b26", "Changing between light and dark Page backgrounds must retain readable text contrast.");
        Require(second.Appearance["workBackgroundImage"].GetString()!.StartsWith("/media/work-starter/", StringComparison.Ordinal), "Background photos must be owned local assets.");
        Require(state.Pages[first.Id].Blocks.Any(block => template.Blocks.Single(type => type.Id == block.Type).Component.EndsWith("CounterBlock", StringComparison.Ordinal)), "The selected special object must actually appear on its Page.");
        Require(state.Pages.Values.Where(page => page.Id != second.Id).All(page => page.Blocks.Any(block => block.Type == "work-navigation"))
            && !state.Pages[second.Id].Blocks.Any(block => block.Type == "work-navigation"), "Each Page's navigation choice must be respected.");
        foreach (var page in state.Pages.Values.Where(page => page.Blocks.Any(block => block.Type == "work-navigation")))
        {
            var renderedOrder = template.Regions.SelectMany(region => page.Blocks.Where(block => block.Region == region.Id)).ToArray();
            Require(renderedOrder[^1].Type == "work-navigation", "Navigation must appear after the complete selected Page layout, including imported regions.");
            var headings = renderedOrder.Where(block => template.Blocks.Single(type => type.Id == block.Type).Component.EndsWith("HeaderBlock", StringComparison.Ordinal)).ToArray();
            Require(headings.Length == 0 || renderedOrder[0] == headings[0], "A chosen header must keep its layout region and appear before Page content.");
        }
        var copiedImageUrl = state.Pages[first.Id].Blocks.SelectMany(block => block.Values).First(pair => pair.Key == "src" && pair.Value.GetString()!.StartsWith("/media/work-starter/", StringComparison.Ordinal)).Value.GetString()!;
        Require(File.Exists(Path.Combine(library, "Media", copiedImageUrl[7..].Replace('/', Path.DirectorySeparatorChar))), "Local media must be copied into the independent project.");
        using (var guide = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(project, ".studio", "project-guide.json"), ct)))
            Require(guide.RootElement.GetProperty("files").EnumerateArray().Any(file => file.GetProperty("relativePath").GetString() == "authoring.template.json" && file.GetProperty("purpose").GetString()!.Length > 30), "The per-file help guide must explain editable source files.");
        await BuildAsync(Path.Combine(project, "src", "Host", "Host.csproj"), Path.Combine(output, "work-build.log"), ct);
        using var host = new HyperlibWorkPreviewHostService();
        var endpoint = await host.EnsureAsync(library, ct);
        await workspace.VerifyEndpointAsync(snapshot.Context, endpoint, ct);
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var preview = await workspace.PreviewAsync(snapshot.Context, state, endpoint, cancellationToken: ct);
        var landingHtml = await client.GetStringAsync(preview.Url, ct);
        Require(landingHtml.Contains(definition.WorkTitle, StringComparison.Ordinal) && landingHtml.Contains("Start exploring this Work.", StringComparison.Ordinal)
            && landingHtml.Contains("hl-work-navigation", StringComparison.Ordinal), "The real Work route must render the customized landing and navigation.");
        var firstPreview = await workspace.PreviewAsync(snapshot.Context, state, endpoint, first.Id, ct);
        var firstHtml = await client.GetStringAsync(firstPreview.Url, ct);
        Require(firstHtml.Contains("Field notes", StringComparison.Ordinal) && firstHtml.Contains("Imported local image", StringComparison.Ordinal), "A generated Page route must render its chosen content and image.");
        var secondPreview = await workspace.PreviewAsync(snapshot.Context, state, endpoint, second.Id, ct);
        var secondHtml = await client.GetStringAsync(secondPreview.Url, ct);
        Require(!secondHtml.Contains("hl-work-navigation", StringComparison.Ordinal) && !secondHtml.Contains("hl-work-pagination", StringComparison.Ordinal), "A Page with navigation disabled must not receive duplicate built-in runtime navigation.");
        Require(secondHtml.Contains("background-image:", StringComparison.Ordinal), "The chosen background image must actually render on the Page.");
        using var media = await client.GetAsync(new Uri(endpoint, copiedImageUrl), ct);
        Require(media.IsSuccessStatusCode, "The private gateway must serve imported photos.");
        var expectedState = await client.GetFromJsonAsync<LibraryState>(new Uri(endpoint, "api/state"), ContentJson.Options, ct) ?? throw new InvalidOperationException("The gateway state is missing.");
        var editedPage = ContentJson.Clone(expectedState.Pages[first.Id]);
        var textBlock = editedPage.Blocks.First(block => template.Blocks.Single(type => type.Id == block.Type).Fields.Any(field => field.Id == "text"));
        textBlock.Values["text"] = ContentJson.Value("Edited through the independent Studio gateway.");
        var updated = await workspace.ApplyAsync(snapshot.Context, endpoint,
            new() { ExpectedRevision = expectedState.Revision, Description = "Verify independent Work content editing", Operations = [new() { Kind = "savePage", Page = editedPage }] }, ct);
        Require(updated.Revision == expectedState.Revision + 1 && updated.Pages[first.Id].Blocks.Any(block => block.Values.TryGetValue("text", out var value) && value.GetString() == "Edited through the independent Studio gateway."), "The generated gateway must save actual Page changes with revision checks.");
        var reopened = await workspace.ReadAsync(project, ct);
        Require(reopened.State!.Revision == updated.Revision, "Studio must reload durable private content after a save.");
        var expanded = await workspace.ApplyAsync(snapshot.Context, endpoint,
            new() { ExpectedRevision = updated.Revision, Description = "Verify navigation follows newly added Pages", Operations = [new() { Kind = "createNode", Id = "new-authored-page", ParentId = workId, NodeType = "page", Title = "A later Page" }] }, ct);
        Require(expanded.Pages["new-authored-page"].Blocks.Any(block => block.Type == "work-navigation"), "Future Pages must inherit a usable navigation object from the Work's defaults.");
        var expandedHtml = await client.GetStringAsync(firstPreview.Url, ct);
        Require(expandedHtml.Contains("A later Page", StringComparison.Ordinal) && expandedHtml.Contains("new-authored-page", StringComparison.Ordinal), "Existing navigation must automatically include a newly authored Page.");
        var sentinel = Path.Combine(parent, "custom-work", "preserve.txt");
        await File.WriteAllTextAsync(sentinel, "existing project data", ct);
        try { await service.CreateAsync(parent, "custom-work", definition, ct); throw new InvalidOperationException("Creating over an existing project should fail."); }
        catch (IOException) { Require(await File.ReadAllTextAsync(sentinel, ct) == "existing project data", "An existing destination must stay intact."); }
        foreach (var marker in sourceMarkers) Require(Hash(marker.Key) == marker.Value, "Generation and preview must not mutate the source library or templates.");
        var result = "HYPERLIB_WORK_PROJECT_OK catalogue|independent-library|wizard-landing|two-distinct-page-layouts|per-page-appearance|contrast|owned-media|background-image|special-object|optional-navigation|auto-new-page-navigation|studio-load|portable-build|real-preview|revision-safe-save|durable-reopen|no-overwrite|source-preserved";
        await File.WriteAllTextAsync(Path.Combine(output, "hyperlib-work-project-result.txt"), result + Environment.NewLine + project, ct);
        return result;
    }
    private static async Task BuildAsync(string project, string log, CancellationToken ct)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.GetDirectoryName(project)!, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "build", project, "--configuration", "Release", "--verbosity", "minimal" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the generated project's build.");
        var standard = process.StandardOutput.ReadToEndAsync(ct); var error = process.StandardError.ReadToEndAsync(ct);
        try { await process.WaitForExitAsync(ct); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        var text = await standard + Environment.NewLine + await error;
        await File.WriteAllTextAsync(log, text, ct);
        Require(process.ExitCode == 0, "The generated Work's portable renderer must build. " + text);
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
