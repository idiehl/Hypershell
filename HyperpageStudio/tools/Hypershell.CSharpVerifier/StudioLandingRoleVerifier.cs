using System.Security.Cryptography;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Services;

public static class StudioLandingRoleVerifier
{
    public static async Task<string> RunAsync(string output, string sourceLibraryRoot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        var sourceIndex = Path.Combine(sourceLibraryRoot, "Content/library.json");
        var sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(sourceIndex, ct));
        var fixture = Path.Combine(output, "lr-" + Guid.NewGuid().ToString("N")[..8]);
        var project = await new HyperlibWorkProjectService(sourceLibraryRoot).CreateAsync(fixture, "Landing role",
            HyperlibWorkProjectService.BuildDirectEditingDefinition("Landing role"), ct);
        var pipeline = (await new HyperlibProjectPipelineService().ReadAsync(project, ct))!;
        var root = Path.GetFullPath(Path.Combine(project, "../.."));
        var store = new ContentStore(root); var state = await store.ReadAsync(ct);
        using var library = new StudioAuthoringLibraryService(sourceLibraryRoot, Path.Combine(fixture, "c"));
        var created = await library.CreateLandingAsync(project, "Welcome", state.Revision, ct: ct);
        state = created.State; var landingId = created.PageId;
        Check(landingId == pipeline.LandingPageId && state.Pages.Count == 1 && state.Pages[landingId].Title == "Welcome",
            "Landing creation names and reuses the reserved empty Page without producing an extra content Page.");
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Operations = [
            new() { Kind = "createNode", Id = "role-volume", NodeType = "volume", ParentId = pipeline.WorkId, Title = "Volume one" },
            new() { Kind = "createNode", Id = "role-chapter", NodeType = "chapter", ParentId = "role-volume", Title = "Chapter one" },
            new() { Kind = "createNode", Id = "role-page", NodeType = "page", ParentId = "role-chapter", Title = "First chapter Page" }
        ] }, ct);
        var added = await library.AddAsync(project, "element:text", landingId, "main", state.Revision, ct: ct);
        state = added.State; var originalBlockId = added.AddedBlockIds.Single();
        var authored = ContentJson.Clone(state.Pages[landingId]);
        authored.Blocks.Single(block => block.Id == originalBlockId).Values["text"] = ContentJson.Value("Keep my authored landing text.");
        authored.Sources.Add(new() { Id = "role-source", Title = "Landing citation", Url = "https://example.com/landing",
            RetrievedAt = DateTimeOffset.UtcNow, BlockIds = [originalBlockId] });
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Operations = [
            new() { Kind = "savePage", Page = authored },
            new() { Kind = "setAppearance", Id = landingId, Appearance = new() { ["background"] = ContentJson.Value("#172635") } }
        ] }, ct);
        var beforeContent = JsonSerializer.Serialize(state.Pages["role-page"], ContentJson.Options);
        var beforeCount = state.Pages.Count;
        var applied = await library.AddAsync(project, "page:hyperlib:work-landing-editorial", "role-page", "main", state.Revision,
            parentId: "role-chapter", ct: ct, targetRole: "landing");
        state = applied.State;
        var work = state.Nodes.Single(node => node.Id == pipeline.WorkId);
        var landingNode = state.Nodes.Single(node => node.Id == landingId);
        Check(applied.PageId == landingId && state.Pages.Count == beforeCount && landingNode.ParentId == work.Id
            && work.Appearance["workLandingPage"].GetString() == landingId,
            "Explicit Landing template placement targets the unique reserved root Page, regardless of the selected Chapter or current content Page.");
        Check(landingNode.Order < state.Nodes.Where(node => node.ParentId == work.Id && node.Id != landingId).Min(node => node.Order),
            "The designated landing has first root order ahead of Volume, Part, Chapter and content Page siblings.");
        Check(state.Pages[landingId].Blocks.Any(block => block.Id == originalBlockId && block.Values["text"].GetString() == "Keep my authored landing text.")
            && state.Pages[landingId].Sources.Single().BlockIds.SequenceEqual([originalBlockId])
            && landingNode.Appearance["background"].GetString() == "#172635"
            && JsonSerializer.Serialize(state.Pages["role-page"], ContentJson.Options) == beforeContent,
            "Applying a landing design preserves existing writing, citations, theme, other Pages and hierarchy.");
        var populated = JsonSerializer.Serialize(state.Pages[landingId], ContentJson.Options);
        var repeated = await library.CreateLandingAsync(project, "Another landing", state.Revision, ct: ct);
        state = repeated.State;
        Check(repeated.PageId == landingId && state.Pages.Count == beforeCount
            && JsonSerializer.Serialize(state.Pages[landingId], ContentJson.Options) == populated,
            "Choosing Landing again opens the existing populated Page without duplicating or overwriting it.");
        var existingIds = state.Pages[landingId].Blocks.Select(block => block.Id).ToArray();
        var secondDesign = await library.AddAsync(project, "page:hyperlib:work-landing-minimal", "role-page", "main", state.Revision,
            parentId: "role-chapter", ct: ct, targetRole: "landing");
        state = secondDesign.State;
        Check(secondDesign.PageId == landingId && state.Pages.Count == beforeCount
            && existingIds.All(id => state.Pages[landingId].Blocks.Any(block => block.Id == id))
            && state.Pages[landingId].Sources.Single().BlockIds.SequenceEqual([originalBlockId])
            && JsonSerializer.Serialize(state.Pages["role-page"], ContentJson.Options) == beforeContent,
            "Repeated template addition retains both earlier design objects and authored text/citations without creating a content Page.");
        populated = JsonSerializer.Serialize(state.Pages[landingId], ContentJson.Options);
        var independent = await library.AddAsync(project, "page:hyperlib:insight-dashboard", landingId, "main", state.Revision,
            "Independent content", "role-chapter", ct);
        state = independent.State;
        Check(independent.PageId != landingId && state.Nodes.Single(node => node.Id == independent.PageId).ParentId == "role-chapter"
            && JsonSerializer.Serialize(state.Pages[landingId], ContentJson.Options) == populated,
            "Ordinary Page creation retains its independent Page identity and chosen content container.");
        var schemaPath = Path.Combine(project, "authoring.template.json"); var schemaHash = SHA256.HashData(await File.ReadAllBytesAsync(schemaPath, ct));
        var stale = false;
        try { await library.AddAsync(project, "page:hyperlib:work-landing-editorial", landingId, "main", state.Revision - 1, ct: ct, targetRole: "landing"); }
        catch (ConflictException) { stale = true; }
        var schemaAfterStale = SHA256.HashData(await File.ReadAllBytesAsync(schemaPath, ct));
        Check(stale && schemaHash.SequenceEqual(schemaAfterStale)
            && (await store.ReadAsync(ct)).Revision == state.Revision,
            "A stale landing-template commit changes neither the source schema nor the content revision.");
        var invalidRole = false;
        try { await library.AddAsync(project, "element:text", landingId, "main", state.Revision, ct: ct, targetRole: "landing"); }
        catch (InvalidDataException) { invalidRole = true; }
        Check(invalidRole && (await store.ReadAsync(ct)).Revision == state.Revision,
            "Only a complete Page template can use the explicit Landing destination.");
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Operations = [
            new() { Kind = "moveNode", Id = landingId, ParentId = "role-chapter" }
        ] }, ct);
        var repaired = await library.CreateLandingAsync(project, null, state.Revision, ct: ct);
        state = repaired.State;
        Check(repaired.PageId == landingId && state.Nodes.Single(node => node.Id == landingId).ParentId == work.Id
            && JsonSerializer.Serialize(state.Pages[landingId], ContentJson.Options) == populated,
            "Opening the Landing role repairs legacy nested placement while preserving its exact authored Page.");
        var staleCreate = false;
        try { await library.CreateLandingAsync(project, "Stale request", state.Revision - 1, ct: ct); }
        catch (ConflictException) { staleCreate = true; }
        Check(staleCreate && (await store.ReadAsync(ct)).Revision == state.Revision,
            "A stale Landing creation request cannot mutate the Work or its landing Page.");
        var workAppearance = ContentJson.Clone(state.Nodes.Single(node => node.Id == work.Id).Appearance);
        workAppearance["workLandingPage"] = ContentJson.Value("missing-role-page");
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Operations = [
            new() { Kind = "setAppearance", Id = work.Id, Appearance = workAppearance }
        ] }, ct);
        var pointerRepair = await library.CreateLandingAsync(project, null, state.Revision, ct: ct);
        state = pointerRepair.State;
        Check(pointerRepair.PageId == landingId && state.Nodes.Single(node => node.Id == work.Id).Appearance["workLandingPage"].GetString() == landingId
            && JsonSerializer.Serialize(state.Pages[landingId], ContentJson.Options) == populated,
            "A stale Work pointer is repaired to the reserved valid landing without changing authored content.");
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Operations = [new() { Kind = "deleteNode", Id = landingId }] }, ct);
        var recovered = await library.CreateLandingAsync(project, "Recovered welcome", state.Revision, ct: ct);
        state = recovered.State;
        Check(recovered.PageId == landingId && state.Pages[landingId].Title == "Recovered welcome"
            && state.Nodes.Single(node => node.Id == landingId).ParentId == work.Id
            && (await new HyperlibProjectPipelineService().ReadAsync(project, ct))!.LandingPageId == landingId
            && state.Pages.ContainsKey(independent.PageId),
            "A missing landing is recreated with the reserved identity and aligned pipeline index while all content Pages survive.");
        var sourceAfter = SHA256.HashData(await File.ReadAllBytesAsync(sourceIndex, ct));
        Check(sourceHash.SequenceEqual(sourceAfter),
            "The registered Hyperlib source content index remains byte-identical after landing creation and role changes.");
        var result = "STUDIO_LANDING_ROLE_OK|checks=" + checks.Count;
        await File.WriteAllTextAsync(Path.Combine(output, "studio-landing-role-result.json"), JsonSerializer.Serialize(new
            { result, checks, project, library = root, workId = work.Id, landingId, contentPageId = independent.PageId }, ContentJson.Options), ct);
        return result;
    }
}
