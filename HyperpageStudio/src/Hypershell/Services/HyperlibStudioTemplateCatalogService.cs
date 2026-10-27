using HyperLib.Core;

namespace Hypershell.Services;

public sealed record HyperlibStudioTemplate(string Id, string Title, string Description, string Category, string ProjectRoot, bool IsBuiltIn);

/// <summary>Local reusable-template operations used by the dedicated Hyperlib Editor workspace.</summary>
public sealed class HyperlibStudioTemplateCatalogService
{
    public Task<IReadOnlyList<HyperlibStudioTemplate>> ListAsync(string libraryRoot, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<HyperlibStudioTemplate>>(ReusableTemplateCatalog.List(libraryRoot)
            .Select(item => Describe(libraryRoot, item)).ToArray());
    }

    public async Task<HyperlibStudioTemplate> CreateBlankAsync(string libraryRoot, string title, CancellationToken ct = default)
        => Describe(libraryRoot, await ReusableTemplateManager.CreateBlankAsync(libraryRoot, title, ct));

    public async Task<HyperlibStudioTemplate> DuplicateAsync(string libraryRoot, string templateId, string title, CancellationToken ct = default)
        => Describe(libraryRoot, await ReusableTemplateManager.DuplicateAsync(libraryRoot, templateId, title, ct));

    public async Task<HyperlibStudioTemplate> CreateFromWorkAsync(string libraryRoot, string workId, string title, CancellationToken ct = default)
        => Describe(libraryRoot, await ReusableTemplateManager.CreateFromWorkAsync(libraryRoot, workId, title, ct));

    public async Task<HyperlibStudioTemplate> CreateFromObjectsAsync(string libraryRoot, string title,
        IEnumerable<TemplateObjectSelection> objects, CancellationToken ct = default)
        => Describe(libraryRoot, await TemplateObjectLibrary.CreateTemplateFromObjectsAsync(libraryRoot, title, objects, ct));

    public Task<string> OpenProjectAsync(string libraryRoot, string templateId, CancellationToken ct = default)
        => ReusableTemplateManager.OpenProjectAsync(libraryRoot, templateId, ct);

    private static HyperlibStudioTemplate Describe(string root, ReusableTemplateInfo item)
        => new(item.Id, item.Title, item.Description, item.Category, ReusableTemplateCatalog.GetProjectRoot(root, item.Id), item.IsBuiltIn);
}
