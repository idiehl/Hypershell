using System.Security.Cryptography;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;

/// <summary>Publication tests touch only generated private fixture libraries, never the registered reader.</summary>
public static class StudioHyperlibWorkPublicationVerifier
{
    public static async Task VerifyAsync(string outputDirectory, string repositoryRoot, string? sourceLibraryRoot = null)
    {
        var fixture = Path.Combine(Path.GetFullPath(outputDirectory), "studio-publication-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var checks = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        var creator = new HyperlibWorkProjectService(sourceLibraryRoot);
        var catalog = creator.GetCatalog();
        var body = catalog.First(item => item.ComponentName is "TextBlock" or "RichTextBlock");
        var headerPiece = catalog.First(item => item.GroupId == "blank" && item.Kind == "element" && item.ComponentName == "CalloutBlock");
        var backgroundBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
        var backgroundPath = Path.Combine(fixture, "publication-background.png");
        await File.WriteAllBytesAsync(backgroundPath, backgroundBytes);
        var request = new HyperlibWorkProjectDefinition
        {
            PipelineVersion = 2, WorkTitle = "Publication fixture", Description = "", SelectedToolsetIds = [body.Id], PageDefinitions = [],
            LandingTemplate = new() { BodyTemplateId = body.Id, NavigationPlacement = "right", ContentAlignment = "center", ContentWidth = "wide",
                NavigationLayout = HyperlibNavigationLayouts.Create("simple-links"), NavigationSticky = true,
                BackgroundSurface = new() { DesignId = "atlas-night", BorderWidth = 2, BorderColor = "#7aa2c6", BorderRadius = 18 },
                HeaderLayout = new() { Enabled = true, Blocks = [
                    new() { Id = "publication-header-brand", Height = 128, Elements = [
                        new() { Id = "publication-header-box", Kind = "box", WidthPercent = 100, Height = 120, FillColor = "#14344a", Padding = 12,
                            Children = [new() { Id = "publication-header-text", Kind = "text", WidthPercent = 70, Text = "Published nested header", FontSize = 30, TextColor = "#ffffff" },
                                new() { Id = "publication-header-image", Kind = "image", WidthPercent = 25, X = 75, Height = 72, MediaPath = backgroundPath, Alt = "Project-owned header image" }] } ] },
                    new() { Id = "publication-header-subtitle", Height = 56, Elements = [
                        new() { Id = "publication-header-subtitle-text", Kind = "text", Text = "A second horizontal header block", Alignment = "center", FontSize = 18 } ] },
                    new() { Id = "publication-header-library", Height = 180, Elements = [
                        new() { Id = "publication-header-library-box", Kind = "box", Height = 170, WidthPercent = 100, Padding = 8,
                            Children = [new() { Id = "publication-header-callout", Kind = "template", TemplateObjectId = headerPiece.Id, TemplateTitle = headerPiece.Title,
                                TemplateValues = new() { ["element"] = new() { ["title"] = ContentJson.Value("Published library header piece"),
                                    ["text"] = ContentJson.Value("A real reusable object inside a header Box.") } } }] } ] }
                ] } },
            PageTemplate = new() { BodyTemplateId = body.Id, NavigationLayout = HyperlibNavigationLayouts.Create("simple-links"),
                BackgroundSurface = new() { DesignId = "image", ImagePath = backgroundPath, ImageFit = "contain", BorderWidth = 3, BorderColor = "#778899", BorderStyle = "dashed" } }
        };
        request.LandingTemplate.BodyLayout = new() { Enabled = true, Blocks = [new() { Height = 240, Elements = [
            new() { Kind = "box", Arrangement = "vertical", WidthPercent = 100, Height = 220, Padding = 12, Children = [
                new() { Text = "Published nested Body", FontSize = 28 },
                new() { Kind = "template", TemplateObjectId = headerPiece.Id, TemplateTitle = headerPiece.Title,
                    TemplateValues = new() { ["element"] = new() { ["title"] = ContentJson.Value("Published library Body piece"), ["text"] = ContentJson.Value("A real object inside a Body Box.") } } }
            ] }
        ] }] };
        request.PageTemplate.BodyLayout = HyperlibBodyLayouts.Create("single-column");
        request.LandingTemplate.BodyLayout.Blocks.Add(new(){Id="publication-slot-row",Height=60,Arrangement="horizontal",Elements=[new(){Id="publication-body-slot",Kind="text",IsContentSlot=true,SlotName="Publication article slot",SlotHint="Add the article text in Authoring.",Text=""}]});
        request.PageTemplate.BodyLayout.Blocks.Add(new(){Id="publication-page-slot-row",Height=60,Arrangement="horizontal",Elements=[new(){Id="publication-page-body-slot",Kind="text",IsContentSlot=true,SlotName="Article text",Text=""}]});
        request.LandingTemplate.FooterLayout=HyperlibFooterLayouts.Create("page-navigation");
        request.PageTemplate.FooterLayout=HyperlibFooterLayouts.Create("page-navigation");
        var project = await creator.CreateAsync(fixture, "Authoring source", request);
        var pipeline = (await new HyperlibProjectPipelineService().ReadAsync(project))!;
        await File.WriteAllTextAsync(Path.Combine(project, ".studio", "authoring-draft.json"), "{\"privateDraft\":\"Unsaved text must not be published.\"}");
        await File.WriteAllTextAsync(Path.Combine(project, ".studio", "recovery-example.json"), "{\"recovery\":true}");
        var sourceRoot = Path.GetFullPath(Path.Combine(project, "../.."));
        var sourceStore = new ContentStore(sourceRoot);
        var state = await sourceStore.ReadAsync();
        var work = state.Nodes.Single(node => node.Type == "work");
        var mediaBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
        var asset = await sourceStore.ImportMediaAsync(new MemoryStream(mediaBytes), "owned-picture.png", "image/png");
        state = await sourceStore.ApplyAsync(new() { ExpectedRevision = state.Revision, Operations =
            [new() { Kind = "updateNode", Id = work.Id, Cover = asset.Url }, new() { Kind = "createNode", Id = "published-chapter", NodeType = "page", ParentId = work.Id, Title = "A saved content chapter" }] });
        var authoredLanding = ContentJson.Clone(state.Pages[pipeline.LandingPageId]);
        authoredLanding.Blocks.Add(new() { Id = "published-authored-text", Type = pipeline.Toolkit.Single(item => item.ChoiceId == body.Id).BlockTypeIds[0],
            Region = "main", Values = new() { ["text"] = ContentJson.Value("Published landing content from Studio.") } });
        state = await sourceStore.ApplyAsync(new() { ExpectedRevision = state.Revision, Operations = [new() { Kind = "savePage", Id = authoredLanding.Id, Page = authoredLanding }] });
        using (var library = new StudioAuthoringLibraryService(creator.TemplateLibraryRoot, Path.Combine(fixture, "c")))
        {
            var landing = await library.AddAsync(project, "page:hyperlib:atlas-work-landing-observatory", pipeline.LandingPageId,
                "main", state.Revision, ct: CancellationToken.None, targetRole: "landing");
            state = landing.State;
            Check(landing.PageId == pipeline.LandingPageId && state.Pages.Count == 2
                && state.Nodes.Single(node => node.Id == work.Id).Appearance["workLandingPage"].GetString() == pipeline.LandingPageId,
                "Adding a new Atlas landing design retains the published Work's canonical landing identity and content Page count.");
        }
        var elementEdits = JsonSerializer.Serialize(new[] { new {
            path = "", attrs = new Dictionary<string, string> { ["data-publication-probe"] = "nested-code" },
            styles = new Dictionary<string, string> { ["border-radius"] = "13px" },
            events = new Dictionary<string, string> { ["click"] = "element.dataset.readerProbe=\"yes\";" }
        } }, new JsonSerializerOptions(ContentJson.Options) { WriteIndented = false });
        var probeSchema = TemplateObjectLibrary.ReadDefinition(project).Blocks.Last(block =>
            block.Component.Split('.').Last() == "CalloutBlock" && block.Fields.Any(field => field.Id == "studioElements"));
        var elementProbe = new PageBlock { Id = "published-element-code-probe", Type = probeSchema.Id, Region = "main",
            Values = probeSchema.Fields.Where(field => field.DefaultValue.HasValue)
                .ToDictionary(field => field.Id, field => field.DefaultValue!.Value.Clone(), StringComparer.Ordinal) };
        elementProbe.Values["studioElements"] = ContentJson.Value(elementEdits);
        var editedLanding = ContentJson.Clone(state.Pages[pipeline.LandingPageId]);
        editedLanding.Blocks.Add(elementProbe);
        state = await sourceStore.ApplyAsync(new() { ExpectedRevision = state.Revision,
            Operations = [new() { Kind = "savePage", Page = editedLanding }] });
        Check(state.Pages[pipeline.LandingPageId].Blocks.Single(block => block.Id == elementProbe.Id).Values["studioElements"].GetString() == elementEdits,
            "The saved landing records Page-local nested element attributes, appearance, and behavior as exact schema-backed JSON.");
        var target = Path.Combine(fixture, "Reader library");
        Directory.CreateDirectory(Path.Combine(target, "Content"));
        await File.WriteAllTextAsync(Path.Combine(target, "hyperlib.app.json"), "{\"schemaVersion\":1,\"id\":\"hyperlib\",\"title\":\"Private publication fixture\"}");
        var original = new LibraryState { LibraryId = "reader-library" };
        await File.WriteAllTextAsync(Path.Combine(target, "Content", "library.json"), JsonSerializer.Serialize(original, ContentJson.Options));
        // Use the stock reader SDK globally. Staged layout behavior must come from the Work's owned SDK.
        CopyDirectory(Path.Combine(creator.TemplateLibraryRoot, "src"), Path.Combine(target, "src"));
        CopyDirectory(Path.Combine(sourceRoot, "Starters"), Path.Combine(target, "Starters"));
        var targetStore = new ContentStore(target); await targetStore.InitializeAsync();
        var targetState = await targetStore.ReadAsync();
        targetState = await targetStore.ApplyAsync(new() { ExpectedRevision = targetState.Revision, Operations =
            [new() { Kind = "createNode", Id = "existing-collection", NodeType = "collection", Title = "Existing collection" },
             new() { Kind = "createNode", Id = "existing-work", NodeType = "work", ParentId = "existing-collection", Title = "Existing Work" }] });
        var preservedPath = Path.Combine(target, "Works", "existing-work", "KEEP.txt"); await File.WriteAllTextAsync(preservedPath, "An independently owned source file.");
        var preserved = SHA256.HashData(await File.ReadAllBytesAsync(preservedPath));
        var publication = new StudioHyperlibWorkPublicationService();
        var reviewed = await publication.ReviewAsync(project, target);
        Check(reviewed.CanPublish && reviewed.PageCount == state.Pages.Count && reviewed.MediaFileCount >= 2 && reviewed.SourceFileCount > 10,
            "Review captures the saved Work branch, Pages, owned code, and only referenced media.");
        var result = await publication.PublishAsync(reviewed);
        var received = await new ContentStore(target).ReadAsync();
        Check(received.Pages[pipeline.LandingPageId].Blocks.Single(block => block.Id == elementProbe.Id).Values["studioElements"].GetString() == elementEdits,
            "Publication preserves the nested element Code, Appearance, and Actions metadata byte for byte in the independent Page.");
        Check(received.Nodes.Single(node => node.Id == work.Id).ParentId == "studio-publications"
            && received.Nodes.Any(node => node.Id == "existing-work") && received.Nodes.Any(node => node.Id == "published-chapter"),
            "Publication attaches stable Work and Page identities under Studio publications and preserves unrelated hierarchy.");
        Check(SHA256.HashData(await File.ReadAllBytesAsync(preservedPath)).SequenceEqual(preserved)
            && File.ReadAllBytes(Path.Combine(target, asset.RelativePath)).SequenceEqual(mediaBytes),
            "Unrelated source is unchanged and the Work's owned media is copied byte for byte.");
        var ownedBackground = received.Nodes.Single(node => node.Id == work.Id).Appearance["studioBackgroundImage"].GetString()!;
        Check(ownedBackground.StartsWith("/media/work-starter/", StringComparison.Ordinal)
            && File.ReadAllBytes(Path.Combine(target, "Media", ownedBackground[7..])).SequenceEqual(backgroundBytes),
            "Publication copies independently owned template background photos through their media schema field.");
        var headerBlock = received.Pages[authoredLanding.Id].Blocks.First(block => block.Type == "studio-header-row");
        var headerRow = JsonSerializer.Deserialize<HyperlibHeaderBlockDefinition>(headerBlock.Values["layout"].GetString()!, ContentJson.Options)!;
        var headerImage = headerRow.Elements.Single().Children.Single(element => element.Kind == "image").MediaPath;
        Check(headerImage.StartsWith("/media/", StringComparison.Ordinal)
            && File.ReadAllBytes(Path.Combine(target, "Media", headerImage[7..])).SequenceEqual(backgroundBytes),
            "Publication discovers and copies media nested inside a header box without exposing native source paths.");
        Check(File.Exists(Path.Combine(result.DestinationWorkRoot, "authoring.template.json"))
            && File.Exists(Path.Combine(result.DestinationWorkRoot, ".studio", "publication.json"))
            && File.Exists(Path.Combine(result.DestinationWorkRoot, "Templates", "StudioRuntime", "HyperLib.Runtime", "HyperLib.Runtime.csproj")),
            "The independent reader Work retains its editable schema, owned rendering SDK, and publication receipt.");
        Check(!File.Exists(Path.Combine(result.DestinationWorkRoot, ".studio", "authoring-draft.json"))
            && !File.Exists(Path.Combine(result.DestinationWorkRoot, ".studio", "recovery-example.json"))
            && File.Exists(Path.Combine(project, ".studio", "authoring-draft.json")),
            "Publication excludes unsaved authoring drafts and recovery files while leaving the original recovery data intact.");
        var journal = Directory.GetFiles(Path.Combine(target, ".hyperlib", "journal"), "*.json").Order().Last();
        using (var record = JsonDocument.Parse(await File.ReadAllTextAsync(journal)))
            Check(record.RootElement.GetProperty("kind").GetString() == "apply" && record.RootElement.GetProperty("schemaVersion").GetInt32() == 1,
                "Publication uses a normal schema 1 apply journal record and remains compatible with undo/recovery.");
        // The private harness marker allows the normal loopback host launcher; this target is still a complete reader library.
        Directory.CreateDirectory(Path.Combine(target, ".studio"));
        await File.WriteAllTextAsync(Path.Combine(target, ".studio", "hyperlib-work.json"), JsonSerializer.Serialize(new { projectKind = "Hyperlib", workId = work.Id }, ContentJson.Options));
        using (var host = new HyperlibWorkPreviewHostService())
        using (var workspace = new HyperlibStudioWorkspaceService())
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
        {
            var endpoint = await host.EnsureAsync(target);
            var opened = await workspace.ReadAsync(result.DestinationWorkRoot);
            var preview = await workspace.PreviewAsync(opened.Context, received, endpoint);
            var html = await http.GetStringAsync(preview.Url);
            Check(html.Contains("data-studio-elements=\"" + WebUtility.HtmlEncode(elementEdits) + "\"", StringComparison.Ordinal),
                "The published reader's server-rendered Page safely embeds its saved nested element metadata.");
            var renderer = await http.GetStringAsync(RendererScriptUri(preview.Url, html));
            Check(renderer.Contains("/* studio-element-edits-v1 */", StringComparison.Ordinal)
                && renderer.Contains("window.hyperlibStudioElements", StringComparison.Ordinal),
                "The actual reader renderer endpoint serves the Work-owned nested element runtime independently of the stock reader SDK.");
            await File.WriteAllTextAsync(Path.Combine(fixture, "published-landing.html"), html);
            Check(html.Contains("Published landing content from Studio.")
                && html.Contains("studio-staged-layout") && html.Contains("data-studio-navigation=\"right\"") && html.Contains("data-studio-alignment=\"center\"")
                && html.Contains("radial-gradient(circle at 8% 14%", StringComparison.Ordinal) && html.Contains("border:2px solid #7aa2c6", StringComparison.Ordinal)
                && html.Contains("Published nested header", StringComparison.Ordinal) && html.Contains("A second horizontal header block", StringComparison.Ordinal)
                && html.Contains("Published library header piece", StringComparison.Ordinal) && html.Contains("A real reusable object inside a header Box.", StringComparison.Ordinal)
                && html.Contains(headerImage, StringComparison.Ordinal),
                "The actual reader Work landing renders its authored object text and staged right navigation/center alignment through its owned SDK.");
            Check(html.Contains("studio-navigation-links", StringComparison.Ordinal) && html.Contains("page/published-chapter", StringComparison.Ordinal)
                && html.Contains("A saved content chapter", StringComparison.Ordinal), "Published Navigation components render real links to the independently owned reader Pages.");
            Check(html.Contains("data-work-landing=\"true\"", StringComparison.Ordinal)
                && html.Contains("ATLAS UNIVERSALIS", StringComparison.Ordinal)
                && html.Contains("data-work-page=\"published-chapter\"", StringComparison.Ordinal)
                && !html.Contains("data-work-page=\"" + pipeline.LandingPageId + "\"", StringComparison.Ordinal),
                "The published Work opens its authored Atlas landing and lists its real content Page without listing itself.");
            Check(html.Contains("Published nested Body", StringComparison.Ordinal) && html.Contains("Published library Body piece", StringComparison.Ordinal)
                && html.Contains("A real object inside a Body Box.", StringComparison.Ordinal), "Published Body rows render nested Boxes and actual imported template objects through the independently owned reader SDK.");
            Check(HasPublishedFooterAndSlots(html),"Published Footer renders a real Next Page route through the owned reader SDK, while private slot and navigation placeholders stay out of reader content.");
            using var mediaResponse = await http.GetAsync(new Uri(endpoint, asset.Url));
            Check(mediaResponse.IsSuccessStatusCode && (await mediaResponse.Content.ReadAsByteArrayAsync()).SequenceEqual(mediaBytes),
                "The published reader gateway serves the independently copied media.");
            await File.WriteAllTextAsync(Path.Combine(fixture, "published-landing.html"), html);
        }
        var copiedSource = Directory.GetFiles(project, "*.razor", SearchOption.AllDirectories).First(path => !path.Contains("\\obj\\") && !path.Contains("\\bin\\"));
        var targetSource = Path.Combine(result.DestinationWorkRoot, Path.GetRelativePath(project, copiedSource));
        var targetSourceHash = SHA256.HashData(await File.ReadAllBytesAsync(targetSource));
        await File.AppendAllTextAsync(copiedSource, "\n@* Authoring source remains independent after publication. *@\n");
        var sourcePage = ContentJson.Clone(state.Pages["published-chapter"]); sourcePage.Title = "Edited after publication";
        state = await sourceStore.ApplyAsync(new() { ExpectedRevision = state.Revision, Operations = [new() { Kind = "savePage", Id = sourcePage.Id, Page = sourcePage }] });
        received = await new ContentStore(target).ReadAsync();
        Check(received.Pages["published-chapter"].Title == "A saved content chapter"
            && SHA256.HashData(await File.ReadAllBytesAsync(targetSource)).SequenceEqual(targetSourceHash),
            "Further Studio content and code edits cannot mutate the already published reader copy.");
        var duplicate = await publication.ReviewAsync(project, target);
        Check(!duplicate.CanPublish && duplicate.ConflictMessage.Contains("already"), "Duplicate publication is rejected before any existing Work can be overwritten.");
        var conflictRejected = false;
        try { await publication.PublishAsync(duplicate); } catch (InvalidOperationException) { conflictRejected = true; }
        Check(conflictRejected && (await new ContentStore(target).ReadAsync()).Revision == received.Revision,
            "A conflicting publication leaves both the reader revision and its content intact.");
        // A fresh target allows explicit source-change and destination-revision conflict checks.
        var conflictTarget = Path.Combine(fixture, "Conflict reader"); Directory.CreateDirectory(Path.Combine(conflictTarget, "Content"));
        await File.WriteAllTextAsync(Path.Combine(conflictTarget, "hyperlib.app.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(conflictTarget, "Content", "library.json"), JsonSerializer.Serialize(new LibraryState { LibraryId = "conflict-reader" }, ContentJson.Options));
        var conflictStore = new ContentStore(conflictTarget); await conflictStore.InitializeAsync();
        var staleReview = await publication.ReviewAsync(project, conflictTarget);
        await File.AppendAllTextAsync(copiedSource, "\n@* Change after review. *@\n");
        var sourceConflict = false; try { await publication.PublishAsync(staleReview); } catch (InvalidOperationException error) { sourceConflict = error.Message.Contains("changed after"); }
        Check(sourceConflict && !(await conflictStore.ReadAsync()).Nodes.Any(node => node.Id == work.Id), "Source changes after review require a fresh review and create no reader Work.");
        var revisionReview = await publication.ReviewAsync(project, conflictTarget);
        var concurrent = await conflictStore.ReadAsync();
        await conflictStore.ApplyAsync(new() { ExpectedRevision = concurrent.Revision, Operations = [new() { Kind = "createNode", Id = "concurrent-collection", NodeType = "collection", Title = "A concurrent reader edit" }] });
        var revisionConflict = false;
        try { await publication.PublishAsync(revisionReview); } catch (ConflictException) { revisionConflict = true; }
        Check(revisionConflict && !Directory.Exists(Path.Combine(conflictTarget, "Works", work.Id)), "Concurrent reader revisions are rejected before copying Work source files.");
        var rollbackReview = await publication.ReviewAsync(project, conflictTarget);
        var conflictingMedia = Path.Combine(conflictTarget, asset.RelativePath); Directory.CreateDirectory(Path.GetDirectoryName(conflictingMedia)!);
        await File.WriteAllTextAsync(conflictingMedia, "Existing conflicting media must stay untouched.");
        var rollbackRejected = false;
        try { await publication.PublishAsync(rollbackReview); } catch (ContentValidationException) { rollbackRejected = true; }
        Check(rollbackRejected && File.ReadAllText(conflictingMedia).StartsWith("Existing conflicting")
            && !Directory.Exists(Path.Combine(conflictTarget, "Works", work.Id))
            && !(await conflictStore.ReadAsync()).Nodes.Any(node => node.Id == work.Id),
            "A media conflict rolls back only newly staged source and preserves the conflicting media and content journal.");
        await File.WriteAllTextAsync(Path.Combine(fixture, "publication-verification.json"), JsonSerializer.Serialize(new { passed = true, checks, project, target, workId = work.Id, pageId = "published-chapter", landingPageId = pipeline.LandingPageId, nestedEditBlockId = elementProbe.Id, nestedElementEdits = elementEdits }, new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "studio-publication-result.txt"), fixture + Environment.NewLine + target + Environment.NewLine + work.Id);
        Console.WriteLine($"STUDIO_HYPERLIB_PUBLICATION_OK|checks:{checks.Count}|{fixture}");
    }
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var path in Directory.GetFiles(source)) File.Copy(path, Path.Combine(destination, Path.GetFileName(path)));
        foreach (var child in Directory.GetDirectories(source))
            if (Path.GetFileName(child) is not ("bin" or "obj" or ".git")) CopyDirectory(child, Path.Combine(destination, Path.GetFileName(child)));
    }

    private static bool HasPublishedFooterAndSlots(string html)
    {
        var start=html.IndexOf("data-region=\"footer\"",StringComparison.Ordinal);
        return start>=0&&html[start..].Contains("data-studio-region=\"footer\"",StringComparison.Ordinal)
            &&html[start..].Contains("href=\"page/published-chapter\"",StringComparison.Ordinal)&&html[start..].Contains("rel=\"next\"",StringComparison.Ordinal)
            &&html.Contains("data-header-element=\"publication-body-slot\"",StringComparison.Ordinal)
            &&!html.Contains("data-studio-slot-placeholder",StringComparison.Ordinal)&&!html.Contains("data-studio-nav-preview-steps",StringComparison.Ordinal);
    }

    private static Uri RendererScriptUri(Uri pageUri, string html)
    {
        var baseMatch = Regex.Match(html, """<base\b[^>]*\bhref\s*=\s*["'](?<url>[^"']+)["']""", RegexOptions.IgnoreCase);
        var baseUri = baseMatch.Success ? new Uri(pageUri, WebUtility.HtmlDecode(baseMatch.Groups["url"].Value)) : pageUri;
        var script = Regex.Match(html, """<script\b[^>]*\bsrc\s*=\s*["'](?<url>[^"']*_content/HyperLib\.Runtime/renderer(?:\.[^/"'?]+)?\.js(?:\?[^"']*)?)["']""", RegexOptions.IgnoreCase);
        if (!script.Success) throw new InvalidOperationException("The rendered reader Page has no owned runtime script.");
        var result = new Uri(baseUri, WebUtility.HtmlDecode(script.Groups["url"].Value));
        if (result.GetLeftPart(UriPartial.Authority) != pageUri.GetLeftPart(UriPartial.Authority))
            throw new InvalidOperationException("The reader runtime script escaped its private fixture origin.");
        return result;
    }

    public static async Task VerifyInstalledReaderAsync(string fixtureDirectory, string executablePath)
    {
        var fixture = Path.GetFullPath(fixtureDirectory);
        using var receipt = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture, "publication-verification.json")));
        var target = receipt.RootElement.GetProperty("target").GetString()!;
        var workId = receipt.RootElement.GetProperty("workId").GetString()!;
        if (!Path.GetFullPath(target).StartsWith(fixture + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Installed-reader verification requires its own disposable target fixture.");
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var start = new ProcessStartInfo(Path.GetFullPath(executablePath)) { WorkingDirectory = target, UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--root"); start.ArgumentList.Add(target); start.ArgumentList.Add("--port"); start.ArgumentList.Add(port.ToString());
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the existing reader executable against the private publication fixture.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            var endpoint = new Uri($"http://127.0.0.1:{port}/");
            var ready = false;
            for (var attempt = 0; attempt < 180 && !process.HasExited; attempt++)
            {
                try { using var health = await http.GetAsync(new Uri(endpoint, "api/health"), timeout.Token); if (health.IsSuccessStatusCode) { ready = true; break; } } catch (HttpRequestException) { }
                await Task.Delay(250, timeout.Token);
            }
            if (!ready) throw new InvalidOperationException("The installed reader could not replay the private publication journal. See installed-reader.log.");
            using var workspace = new HyperlibStudioWorkspaceService();
            var opened = await workspace.ReadAsync(Path.Combine(target, "Works", workId), timeout.Token);
            var preview = await workspace.PreviewAsync(opened.Context, opened.State!, endpoint, cancellationToken: timeout.Token);
            var html = await http.GetStringAsync(preview.Url, timeout.Token);
            var elementEdits = receipt.RootElement.GetProperty("nestedElementEdits").GetString()!;
            var landingId = receipt.RootElement.GetProperty("landingPageId").GetString()!;
            var probeId = receipt.RootElement.GetProperty("nestedEditBlockId").GetString()!;
            if (opened.State!.Pages[landingId].Blocks.Single(block => block.Id == probeId).Values["studioElements"].GetString() != elementEdits
                || !html.Contains("data-studio-elements=\"" + WebUtility.HtmlEncode(elementEdits) + "\"", StringComparison.Ordinal))
                throw new InvalidOperationException("The installed reader did not preserve and render Page-local nested element metadata.");
            var renderer = await http.GetStringAsync(RendererScriptUri(preview.Url, html), timeout.Token);
            if (!renderer.Contains("/* studio-element-edits-v1 */", StringComparison.Ordinal)
                || !renderer.Contains("window.hyperlibStudioElements", StringComparison.Ordinal))
                throw new InvalidOperationException("The installed reader did not serve the published Work's owned nested element runtime.");
            if (!html.Contains("Published landing content from Studio.") || !html.Contains("data-studio-navigation=\"right\"") || !html.Contains("data-studio-alignment=\"center\"")
                || !html.Contains("radial-gradient(circle at 8% 14%", StringComparison.Ordinal) || !html.Contains("border:2px solid #7aa2c6", StringComparison.Ordinal))
                throw new InvalidOperationException("The installed reader did not preserve the published Work's landing route and staged placements.");
            if (!html.Contains("Published nested header", StringComparison.Ordinal) || !html.Contains("A second horizontal header block", StringComparison.Ordinal))
                throw new InvalidOperationException("The installed reader did not render the published nested header and its second block.");
            if (!html.Contains("Published library header piece", StringComparison.Ordinal) || !html.Contains("A real reusable object inside a header Box.", StringComparison.Ordinal))
                throw new InvalidOperationException("The installed reader did not render the actual imported template object inside the header Box.");
            if (!html.Contains("studio-navigation-links", StringComparison.Ordinal) || !html.Contains("page/published-chapter", StringComparison.Ordinal))
                throw new InvalidOperationException("The installed reader did not compile and render the published Navigation components and Page links.");
            if (!html.Contains("Published nested Body", StringComparison.Ordinal) || !html.Contains("Published library Body piece", StringComparison.Ordinal))
                throw new InvalidOperationException("The installed reader did not render the published Body layout and its nested template object.");
            if(!HasPublishedFooterAndSlots(html))throw new InvalidOperationException("The installed reader did not render real published Footer navigation, or exposed private slot/navigation placeholders.");
            if (!html.Contains("data-work-landing=\"true\"", StringComparison.Ordinal) || !html.Contains("ATLAS UNIVERSALIS", StringComparison.Ordinal)
                || !html.Contains("data-work-page=\"published-chapter\"", StringComparison.Ordinal))
                throw new InvalidOperationException("The installed reader did not open the new authored Atlas Work landing and its dynamic content Page directory.");
            var media = (await new ContentStore(target).ReadAsync(timeout.Token)).Nodes.Single(node => node.Id == workId).Cover!;
            using var mediaResponse = await http.GetAsync(new Uri(endpoint, media), timeout.Token);
            if (!mediaResponse.IsSuccessStatusCode) throw new InvalidOperationException("The installed reader did not serve the independently copied media.");
            await File.WriteAllTextAsync(Path.Combine(fixture, "installed-reader-published-landing.html"), html);
            await File.WriteAllTextAsync(Path.Combine(fixture, "installed-reader-verification.json"), JsonSerializer.Serialize(new
            { passed = true, executablePath, target, workId, previewUrl = preview.Url.AbsoluteUri, journalReplay = true, ownedSdkLanding = true, stagedPlacements = true, realFooterNavigation=true, privateSlotLabelsOmitted=true, nestedElementMetadata = true, ownedNestedElementRuntime = true, media = true }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"STUDIO_INSTALLED_READER_PUBLICATION_OK|{fixture}");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await File.WriteAllTextAsync(Path.Combine(fixture, "installed-reader.log"), await output + Environment.NewLine + await error);
        }
    }
}
