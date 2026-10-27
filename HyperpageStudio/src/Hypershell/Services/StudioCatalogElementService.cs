using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Models;

namespace Hypershell.Services;

/// <summary>Adapts the existing HTML/Razor catalog without replacing its descriptors or changing the open Work on selection.</summary>
public sealed class StudioCatalogElementService : IDisposable
{
    private sealed record CatalogEntry(StudioAuthoringLibraryItem Item, HyperlibLegacyTemplateModule? Module, VisualPageTemplateDocument? Source);
    private readonly VisualPageComponentCatalog _catalog = new();
    private readonly Dictionary<string, CatalogEntry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _previewProjects = [];
    private readonly SemaphoreSlim _previewGate = new(1, 1);
    private readonly string _sourceLibrary;
    private readonly string _cacheRoot;
    private readonly HyperlibTemplateObjectPreviewService _previews;
    private bool _disposed;

    public StudioCatalogElementService(string sourceLibrary, string? cacheRoot = null)
    {
        _sourceLibrary = Path.GetFullPath(sourceLibrary);
        _cacheRoot = Path.GetFullPath(cacheRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hypershell", "Hyperlib", "catalog-element-preview", Guid.NewGuid().ToString("N")));
        // Generated fingerprints add another directory level; a short segment keeps CreateProcess's working directory below its Windows limit.
        _previews = new(Path.Combine(_cacheRoot, "r"));
    }

    public StudioAuthoringLibraryItem Describe(string descriptorId) => Entry(descriptorId).Item;

    public async Task<StudioAuthoringLibraryPreview> PreviewAsync(string descriptorId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var selected = Required(descriptorId);
        await _previewGate.WaitAsync(ct);
        try
        {
            // Each bounded batch shares a real compiled renderer. Browsing does not write to any user Work or source collection.
            var supported = _catalog.Descriptors.Select(descriptor => Entry(descriptor.Id)).Where(entry => entry.Module is not null).ToArray();
            var index = Array.FindIndex(supported, entry => entry.Item.Id == selected.Item.Id);
            var batch = index / 140;
            if (!_previewProjects.TryGetValue(batch, out var project))
            {
                project = await new HyperlibWorkProjectService(_sourceLibrary).CreateAsync(Path.Combine(_cacheRoot, "projects"),
                    "catalog-" + batch + "-" + Guid.NewGuid().ToString("N"), HyperlibWorkProjectService.BuildDirectEditingDefinition("Element preview"), ct);
                var definition = TemplateObjectLibrary.ReadDefinition(project);
                foreach (var entry in supported.Skip(batch * 140).Take(140))
                {
                    definition.Blocks.Add(ContentJson.Clone(entry.Module!.Definition));
                    foreach (var file in Files(entry.Module, entry.Source!))
                    {
                        var path = HyperlibProjectPipelineService.Guard(project, file.RelativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        await File.WriteAllBytesAsync(path, file.Content, ct);
                    }
                }
                HyperlibProjectPipelineService.EnsureDirectEditorFields(definition);
                TemplateValidator.ValidateDefinition(definition);
                await HyperlibProjectPipelineService.AtomicWriteAsync(Path.Combine(project, "authoring.template.json"), JsonSerializer.Serialize(definition, ContentJson.Options), ct);
                _previewProjects[batch] = project;
            }
            var library = Path.GetFullPath(Path.Combine(project, "../.."));
            var preview = await _previews.PreviewElementAsync(library, project, selected.Module!.Definition.Id, ct);
            return new(preview.Url.AbsoluteUri, selected.Item.Title,
                "Element preview. Add to Page places an independently editable copy in the current Page.");
        }
        finally { _previewGate.Release(); }
    }

    /// <summary>Installs a deterministic, portable component. Page insertion remains in the caller's revision-checked content transaction.</summary>
    public async Task<BlockDefinition> PrepareInsertionAsync(string projectRoot, TemplateDefinition definition, string descriptorId, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var entry = Required(descriptorId);
        var module = entry.Module!;
        var existing = definition.Blocks.SingleOrDefault(block => block.Id == module.Definition.Id);
        if (existing is not null) return existing;
        var definitionBytes = await File.ReadAllBytesAsync(Path.Combine(projectRoot, "authoring.template.json"), ct);
        var actual = JsonSerializer.Deserialize<TemplateDefinition>(definitionBytes, ContentJson.Options)
            ?? throw new InvalidDataException("The destination element schema is empty.");
        if (JsonSerializer.Serialize(actual, ContentJson.Options) != JsonSerializer.Serialize(definition, ContentJson.Options))
            throw new InvalidDataException("The Template changed on disk. Refresh the Page before adding the element.");
        var hash = Convert.ToHexString(SHA256.HashData(definitionBytes));
        var next = ContentJson.Clone(definition);
        next.Blocks.Add(ContentJson.Clone(module.Definition));
        HyperlibProjectPipelineService.EnsureDirectEditorFields(next);
        await HyperlibStudioAuthoringService.CheckCompatibilityAsync(projectRoot, next);
        var plan = new TemplateImportPlan { TargetProjectRoot = projectRoot, Definition = next,
            Files = Files(module, entry.Source!), SourceFiles = [], TargetFiles = [], Summary = "Catalog element " + entry.Item.Title };
        await TemplateObjectImporter.CommitAsync(projectRoot, next, [plan], hash, ct);
        return next.Blocks.Single(block => block.Id == module.Definition.Id);
    }

    private CatalogEntry Required(string id)
    {
        var entry = Entry(id);
        if (!entry.Item.Compatible) throw new InvalidDataException(entry.Item.Reason);
        return entry;
    }

    private CatalogEntry Entry(string id)
    {
        if (id.StartsWith("catalog:", StringComparison.Ordinal)) id = id[8..];
        lock (_entries)
        {
            if (_entries.TryGetValue(id, out var entry)) return entry;
            var descriptor = _catalog.GetRequired(id);
            var reason = UnsupportedReason(descriptor);
            HyperlibLegacyTemplateModule? module = null;
            VisualPageTemplateDocument? source = null;
            if (reason is null)
            {
                try
                {
                    source = new() { TemplateId = "catalog-v1-" + descriptor.Id, Name = descriptor.DisplayName,
                        Description = descriptor.Description, Library = "Studio element catalog", Scope = "element", Category = descriptor.Category,
                        Root = _catalog.CreateElementForAuthoring(descriptor.Id) };
                    var code = PrepareRazorState(source.Root);
                    module = HyperlibLegacyTemplateLibraryService.ConvertForAuthoring(source, code);
                    const string oldPlaceholder = "/_content/HyperLib.Work/legacy/placeholder.svg";
                    const string catalogPlaceholder = "/_content/HyperLib.Work/catalog/studio-element-placeholder-v1.svg";
                    module = module with { Razor = module.Razor.Replace(oldPlaceholder, catalogPlaceholder, StringComparison.Ordinal) };
                    foreach (var field in module.Definition.Fields)
                        if (field.DefaultValue is { ValueKind: JsonValueKind.String } value && value.GetString() == oldPlaceholder)
                            field.DefaultValue = ContentJson.Value(catalogPlaceholder);
                    module.Definition.CatalogId = "STUDIO-ELEMENT-" + descriptor.Id.Replace('.', '-').ToUpperInvariant();
                    module.Definition.SourceTemplateId = descriptor.Id;
                    module.Definition.SourceLibrary = "Studio element catalog";
                }
                catch (Exception exception) when (exception is VisualPageValidationException or ContentValidationException or InvalidDataException)
                { reason = "This element requires additional source setup before it can be placed: " + exception.Message; }
            }
            var item = new StudioAuthoringLibraryItem("catalog:" + descriptor.Id, descriptor.DisplayName, descriptor.Description, "element",
                descriptor.Category, descriptor.Authoring.Badge, descriptor.MarkupName, reason is null, reason);
            entry = new(item, module, source);
            _entries.Add(id, entry);
            return entry;
        }
    }

    private static string? UnsupportedReason(VisualPageComponentDescriptor descriptor)
    {
        if (descriptor.IsDocumentOnly) return "This element belongs to the document shell. Edit the project's HTML or Razor source to configure it.";
        if (descriptor.Id is "core.condition" or "core.loop") return "This Razor element needs a project expression and data source. Configure it in the code editor before placing it.";
        if (descriptor.Id is "custom.razor" or "custom.component") return "This element requires reviewed custom source or a component type. Add that source in the code editor first.";
        if (descriptor.MarkupName is "script" or "style") return "Use the project's JavaScript or CSS file for this source element.";
        if (descriptor.Id is "core.validation-message" or "core.validation-summary") return "Validation elements need a configured form and field. Add them inside the form's Razor source.";
        return null;
    }

    private static string PrepareRazorState(VisualPageElement root)
    {
        var fields = new StringBuilder();
        var index = 0;
        void Visit(VisualPageElement element)
        {
            var number = index++;
            if (element.DescriptorId == "core.form")
            {
                element.Attributes.Remove("Model"); element.Attributes.Remove("OnValidSubmit");
                element.Bindings.Add(new() { Kind = VisualPageBindingKind.Attribute, Target = "Model", Expression = "CatalogModel" });
            }
            if (element.DescriptorId is "core.input-text" or "core.input-number" or "core.input-checkbox" or "core.select")
            {
                var name = "CatalogValue" + number;
                var (type, value) = element.DescriptorId switch
                { "core.input-number" => ("double", "0"), "core.input-checkbox" => ("bool", "false"), _ => ("string", "string.Empty") };
                fields.AppendLine($"private {type} {name} {{ get; set; }} = {value};");
                element.Bindings.Add(new() { Kind = VisualPageBindingKind.Value, Expression = name });
            }
            foreach (var child in element.Children) Visit(child);
        }
        Visit(root);
        return "@using Microsoft.AspNetCore.Components.Forms\n@code {\nprivate CatalogStarterModel CatalogModel { get; } = new();\nprivate sealed class CatalogStarterModel { public string Text { get; set; } = string.Empty; }\n" + fields + "\n}";
    }

    private static List<TemplateImportFile> Files(HyperlibLegacyTemplateModule module, VisualPageTemplateDocument source)
    {
        var result = new List<TemplateImportFile>();
        void Add(string path, string content)
        { var bytes = Encoding.UTF8.GetBytes(content); result.Add(new(path, bytes, Convert.ToHexString(SHA256.HashData(bytes)))); }
        var prefix = "src/Pages/Blocks/" + module.Definition.Component;
        Add(prefix + ".razor", module.Razor);
        Add(prefix + ".razor.css", module.Css);
        Add(".studio/catalog-elements/" + module.Definition.Id + ".json", JsonSerializer.Serialize(source, VisualPageJson.Options));
        Add("src/Pages/wwwroot/catalog/studio-element-placeholder-v1.svg", PlaceholderSvg);
        return result;
    }

    public void Dispose() { if (_disposed) return; _disposed = true; _previews.Dispose(); }

    private const string PlaceholderSvg = """
<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="750" viewBox="0 0 1200 750"><defs><linearGradient id="sky" x2="1" y2="1"><stop stop-color="#172845"/><stop offset="1" stop-color="#668f9a"/></linearGradient></defs><rect width="1200" height="750" fill="url(#sky)"/><circle cx="875" cy="205" r="70" fill="#edcf85"/><path d="M0 610 300 260 560 555 775 385 1200 650V750H0Z" fill="#243c48"/><path d="M0 670 360 445 750 665 995 500 1200 620V750H0Z" fill="#376060"/><text x="60" y="690" font-family="system-ui,sans-serif" font-size="24" fill="#f7eedc">Replace with your own image</text></svg>
""";
}
