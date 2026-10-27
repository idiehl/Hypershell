using System.Text;
using System.Text.Json;

namespace HyperLib.Core;

/// <summary>Publishes complete local template projects atomically. Source Works and templates are never modified.</summary>
public static class ReusableTemplateManager
{
    public static async Task<string> OpenProjectAsync(string libraryRoot, string id, CancellationToken ct = default)
    {
        var item = ReusableTemplateCatalog.Find(libraryRoot, id);
        var root = ReusableTemplateCatalog.GetProjectRoot(libraryRoot, id);
        if (item.IsBuiltIn)
            await TemplateProjectScaffolder.EnsureAsync(libraryRoot, root, "template-" + id, item.Title, ct, id, exportingCatalog: true);
        _ = ReusableTemplateCatalog.ReadDefinition(libraryRoot, id);
        return root;
    }

    public static Task<ReusableTemplateInfo> CreateBlankAsync(string libraryRoot, string title, CancellationToken ct = default)
        => CreateAsync(libraryRoot, title, "An empty reusable design with the standard elements.", null,
            ReusableTemplateCatalog.CreateBuiltInDefinition(ReusableTemplateCatalog.BlankId), ct);

    public static async Task<ReusableTemplateInfo> DuplicateAsync(string libraryRoot, string id, string title, CancellationToken ct = default)
    {
        ContentRules.ValidateTitle(title);
        var original = ReusableTemplateCatalog.Find(libraryRoot, id);
        var source = await OpenProjectAsync(libraryRoot, id, ct);
        return await CreateAsync(libraryRoot, title, "Reusable design copied from " + original.Title + ".", source,
            ReusableTemplateCatalog.ReadDefinition(libraryRoot, id), ct);
    }

    public static async Task<ReusableTemplateInfo> CreateFromWorkAsync(string libraryRoot, string workId, string title, CancellationToken ct = default)
    {
        ContentRules.ValidateTitle(title);
        ContentRules.ValidateNodeId(workId);
        var store = new ContentStore(libraryRoot);
        var state = ReusableTemplateCatalog.ReadJson<LibraryState>(store.ResolvePath("Content/library.json"), value => ContentRules.Validate(value));
        var work = state.Nodes.FirstOrDefault(node => node.Type == "work" && node.Id == workId)
            ?? throw new ContentValidationException("Choose a Work from this library.");
        var source = store.ResolvePath("Works/" + work.Id);
        await TemplateProjectScaffolder.EnsureAsync(libraryRoot, source, work.Id, work.Title, ct, work.StarterId);
        var definition = ReusableTemplateCatalog.ReadJson<TemplateDefinition>(Path.Combine(source, "authoring.template.json"), TemplateValidator.ValidateDefinition);
        return await CreateAsync(libraryRoot, title, "Reusable design copied from " + work.Title + ".", source, definition, ct);
    }

    private static async Task<ReusableTemplateInfo> CreateAsync(string libraryRoot, string title, string description,
        string? source, TemplateDefinition definition, CancellationToken ct)
    {
        ContentRules.ValidateTitle(title);
        ct.ThrowIfCancellationRequested();
        var id = "custom-" + Guid.NewGuid().ToString("N");
        var parent = TemplatePathGuard.Within(libraryRoot, Path.Combine(libraryRoot, "Templates"));
        var staging = TemplatePathGuard.Within(libraryRoot, Path.Combine(parent, ".pending-" + Guid.NewGuid().ToString("N")));
        var project = Path.Combine(staging, "Project");
        Directory.CreateDirectory(project);
        // Interrupted attempts stay outside the catalog; never publish partially copied source.
        if (source is null)
            await TemplateProjectScaffolder.EnsureAsync(libraryRoot, project, "template-" + id, title.Trim(), ct, ReusableTemplateCatalog.BlankId, exportingCatalog: true);
        else
        {
            TemplatePathGuard.Within(libraryRoot, source);
            await TemplateProjectScaffolder.CopyProjectSourceAsync(libraryRoot, source, project, ct);
            await TemplateProjectScaffolder.CopyProjectManifestAsync(libraryRoot, source, project, "template-" + id, title.Trim(), ct);
        }
        definition = ContentJson.Clone(definition);
        definition.Title = title.Trim();
        TemplateValidator.ValidateDefinition(definition);
        await TemplatePathGuard.ReplaceAsync(Path.Combine(project, "authoring.template.json"), JsonSerializer.SerializeToUtf8Bytes(definition, ContentJson.Options), ct);
        var item = new ReusableTemplateInfo(id, title.Trim(), description, "My templates", false);
        await TemplatePathGuard.WriteNewAsync(Path.Combine(staging, "template.json"), JsonSerializer.SerializeToUtf8Bytes(item, ContentJson.Options), ct);
        await TemplatePathGuard.WriteNewAsync(Path.Combine(staging, "README.md"), Encoding.UTF8.GetBytes(
            $"# {item.Title}\n\n{description}\n\nOpen Project/visual-page.project.json in Hyperpage Studio. Changes are used by new Works; existing Works retain their independent source and content.\n"), ct);
        ct.ThrowIfCancellationRequested();
        var destination = TemplatePathGuard.Within(libraryRoot, Path.Combine(parent, id));
        TemplatePathGuard.NoLinks(staging);
        Directory.Move(staging, destination);
        return item;
    }
}
