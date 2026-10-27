using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Services;

public static class StudioCatalogElementsVerifier
{
    public static async Task<string> RunAsync(string output, string sourceLibrary, CancellationToken ct = default)
    {
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        static Dictionary<string, string> Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or ".hyperlib"))
            .ToDictionary(path => Path.GetRelativePath(root, path), Hash, StringComparer.OrdinalIgnoreCase);
        static bool Equal(Dictionary<string, string> left, Dictionary<string, string> right) => left.Count == right.Count && left.All(pair => right.GetValueOrDefault(pair.Key) == pair.Value);
        var sourceIndex = Path.Combine(sourceLibrary, "Content", "library.json");
        var sourceHash = Hash(sourceIndex);
        var project = await new HyperlibWorkProjectService(sourceLibrary).CreateAsync(Path.Combine(output, "owned-work"), "site",
            HyperlibWorkProjectService.BuildDirectEditingDefinition("Catalog Elements verification"), ct);
        var library = Path.GetFullPath(Path.Combine(project, "../.."));
        var pipeline = (await new HyperlibProjectPipelineService().ReadAsync(project, ct))!;
        var pageId = pipeline.LandingPageId;
        var store = new ContentStore(library);
        var state = await store.ReadAsync(ct);
        using var service = new StudioAuthoringLibraryService(sourceLibrary, Path.Combine(output, "catalog-cache"));
        var catalog = new VisualPageComponentCatalog();
        var items = catalog.Descriptors.Select(descriptor => service.DescribeCatalogElement(descriptor.Id)).ToArray();
        Check(items.Length == catalog.Descriptors.Count && items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() == items.Length,
            "Every existing descriptor keeps one canonical catalog identity and its own compatibility status.");
        Check(catalog.Descriptors.Zip(items).All(pair => pair.Second.Id == "catalog:" + pair.First.Id && pair.Second.Title == pair.First.DisplayName
            && pair.Second.Category == pair.First.Category && pair.Second.Kind == pair.First.MarkupName),
            "Catalog adaptation preserves actual names, markup types, category and authoritative descriptor identity.");
        Check(new[] { "core.button", "core.image", "core.input-checkbox", "core.form", "core.input-text", "core.select" }
            .All(id => items.Single(item => item.Id == "catalog:" + id).Compatible), "Common HTML, nested image and typed Razor controls are insertable actual elements.");
        Check(new[] { "custom.razor", "custom.component", "core.condition", "core.loop", "core.validation-message" }
            .All(id => items.Single(item => item.Id == "catalog:" + id) is { Compatible: false, Reason.Length: > 20 }),
            "Context-dependent source and logic elements give a precise reason rather than generating a substitute object.");
        var table = catalog.Descriptors.First(descriptor => descriptor.MarkupName == "table");
        var samples = new[] { "core.button", table.Id, "core.input-checkbox", "core.form", "core.input-text", "core.select" }
            .Concat(items.Where(item => item.Compatible).Where((_, index) => index % 140 == 0).Select(item => item.Id[8..]))
            .Distinct(StringComparer.Ordinal).ToArray();
        var projectBefore = Snapshot(project);
        var libraryBefore = Hash(Path.Combine(library, "Content", "library.json"));
        var previews = new List<object>();
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        foreach (var descriptor in samples)
        {
            var preview = await service.PreviewAsync(project, "catalog:" + descriptor, ct);
            using var response = await http.GetAsync(preview.Url, ct);
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(ct));
            Check(response.IsSuccessStatusCode && html.Contains("hl-block", StringComparison.Ordinal) && !html.Contains("data-preview-error", StringComparison.Ordinal),
                "Compiled, isolated catalog preview renders " + descriptor + " without a generic fallback.");
            if (descriptor == "core.input-checkbox") Check(html.Contains("type=\"checkbox\"", StringComparison.Ordinal), "The Razor checkbox preview is the real compiled InputCheckbox output.");
            if (descriptor == table.Id) Check(html.Contains("<table", StringComparison.Ordinal) && html.Contains("<tr", StringComparison.Ordinal), "Table preview retains its real starter child structure.");
            await File.WriteAllTextAsync(Path.Combine(output, descriptor.Replace('.', '-') + "-preview.html"), html, ct);
            previews.Add(new { descriptor, preview.Url });
        }
        Check(Equal(projectBefore, Snapshot(project)) && Hash(Path.Combine(library, "Content", "library.json")) == libraryBefore,
            "Selecting and previewing every representative catalog family changes no Work source, schema, metadata, Page or library revision.");
        var unsupported = false;
        try { await service.AddAsync(project, "catalog:custom.component", pageId, "main", state.Revision, ct: ct); }
        catch (InvalidDataException error) when (error.Message.Contains("source", StringComparison.OrdinalIgnoreCase)) { unsupported = true; }
        Check(unsupported && Equal(projectBefore, Snapshot(project)), "Unsupported Add is rejected before schema or source installation.");
        var stale = false;
        try { await service.AddAsync(project, "catalog:core.button", pageId, "main", state.Revision - 1, ct: ct); }
        catch (ConflictException) { stale = true; }
        Check(stale && Equal(projectBefore, Snapshot(project)), "A stale Page revision is rejected before catalog component files or schema change.");
        var canceled = false;
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            try { await service.AddAsync(project, "catalog:core.button", pageId, "main", state.Revision, ct: cancel.Token); }
            catch (OperationCanceledException) { canceled = true; }
        }
        Check(canceled && Equal(projectBefore, Snapshot(project)), "Canceled catalog actions preserve the complete Work.");
        var addedIds = new List<string>();
        foreach (var id in new[] { "core.button", table.Id, "core.input-checkbox", "core.form", "core.input-text", "core.select" })
        {
            var count = state.Pages[pageId].Blocks.Count;
            var added = await service.AddAsync(project, "catalog:" + id, pageId, "main", state.Revision, ct: ct, insertionY: addedIds.Count * 300);
            Check(added.PageId == pageId && added.AddedBlockIds.Count == 1 && added.State.Pages[pageId].Blocks.Count == count + 1,
                "Add commits one independent " + id + " instance to the current Page.");
            state = added.State; addedIds.Add(added.AddedBlockIds.Single());
            var block = state.Pages[pageId].Blocks.Single(block => block.Id == added.AddedBlockIds.Single());
            var schema = TemplateObjectLibrary.ReadDefinition(project).Blocks.Single(schema => schema.Id == block.Type);
            Check(schema.SourceTemplateId == id && schema.SourceLibrary == "Studio element catalog" && File.Exists(Path.Combine(project, "src", "Pages", "Blocks", schema.Component + ".razor")),
                "Committed " + id + " retains actual descriptor provenance and owned portable Razor source.");
            if (id == table.Id)
            {
                var astPath = Path.Combine(project, ".studio", "catalog-elements", schema.Id + ".json");
                using var ast = JsonDocument.Parse(await File.ReadAllTextAsync(astPath, ct));
                Check(ast.RootElement.GetProperty("root").GetProperty("children").GetArrayLength() == catalog.CreateElementForAuthoring(id).Children.Count,
                    "The committed table AST retains its complete starter children rather than flattening into text.");
            }
        }
        var button = state.Pages[pageId].Blocks.Single(block => block.Id == addedIds[0]);
        var buttonSchema = TemplateObjectLibrary.ReadDefinition(project).Blocks.Single(schema => schema.Id == button.Type);
        var textField = buttonSchema.Fields.First(field => field.Id.StartsWith("text-", StringComparison.Ordinal)).Id;
        var authored = ContentJson.Clone(state.Pages[pageId]);
        authored.Blocks.Single(block => block.Id == button.Id).Values[textField] = ContentJson.Value("A saved catalog button");
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Edit actual catalog property", Operations = [new() { Kind = "savePage", Id = pageId, Page = authored }] }, ct);
        var copy = await service.AddAsync(project, "catalog:core.button", pageId, "main", state.Revision, ct: ct);
        state = copy.State;
        Check(copy.AddedBlockIds.Single() != button.Id && state.Pages[pageId].Blocks.Single(block => block.Id == button.Id).Values[textField].GetString() == "A saved catalog button"
            && state.Pages[pageId].Blocks.Single(block => block.Id == copy.AddedBlockIds.Single()).Values[textField].GetString() == "Button",
            "Repeated insertion creates fresh block identities and never overwrites previously authored field values.");
        var reopened = await new ContentStore(library).ReadAsync(ct);
        Check(reopened.Revision == state.Revision && reopened.Pages[pageId].Blocks.Count == state.Pages[pageId].Blocks.Count,
            "Committed elements and authored values survive a fresh content-store read.");
        using (var hosts = new HyperlibWorkPreviewHostService())
        {
            var endpoint = await hosts.EnsureAsync(library, ct);
            using var runtimeResponse = await http.GetAsync(new Uri(endpoint, "api/runtime/" + pipeline.WorkId), ct);
            var runtimeText = await runtimeResponse.Content.ReadAsStringAsync(ct);
            using var runtime = JsonDocument.Parse(runtimeText);
            Check(runtimeResponse.IsSuccessStatusCode && runtime.RootElement.TryGetProperty("proxyPrefix", out _), "The installed Work compiles all inserted HTML and typed Razor components together.");
            var prefix = runtime.RootElement.GetProperty("proxyPrefix").GetString()!;
            var html = WebUtility.HtmlDecode(await http.GetStringAsync(new Uri(endpoint, prefix.TrimStart('/') + "page/" + pageId + "?edit=1"), ct));
            Check(html.Contains("A saved catalog button", StringComparison.Ordinal) && html.Contains("<table", StringComparison.Ordinal)
                && html.Contains("type=\"checkbox\"", StringComparison.Ordinal) && !html.Contains("data-render-error", StringComparison.Ordinal),
                "The reopened real Page renders the independently edited HTML object, nested table and actual Razor checkbox.");
        }
        Check(Hash(sourceIndex) == sourceHash, "The source Hyperlib library remains unchanged after previews, commits and compiled Page verification.");
        var result = "STUDIO_CATALOG_ELEMENTS_OK|checks=" + checks.Count;
        await File.WriteAllTextAsync(Path.Combine(output, "studio-catalog-elements-result.json"), JsonSerializer.Serialize(new { result, checks, project, library, pageId, previews,
            descriptorCount = items.Length, supported = items.Count(item => item.Compatible), unsupported = items.Where(item => !item.Compatible) }, new JsonSerializerOptions { WriteIndented = true }), ct);
        return result;
    }
}
