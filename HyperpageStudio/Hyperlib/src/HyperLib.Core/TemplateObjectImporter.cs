using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HyperLib.Core;

public sealed record TemplateObjectSelection(string GroupId, string ObjectId);
public sealed record TemplateImportFile(string RelativePath, byte[] Content, string Sha256);
public sealed record TemplateSourceFingerprint(string Path, string Sha256);
public sealed class TemplateImportPlan
{
    public required string TargetProjectRoot { get; init; }
    public required TemplateDefinition Definition { get; init; }
    public required IReadOnlyList<TemplateImportFile> Files { get; init; }
    public required IReadOnlyList<TemplateSourceFingerprint> SourceFiles { get; init; }
    public required IReadOnlyList<TemplateSourceFingerprint> TargetFiles { get; init; }
    public required string Summary { get; init; }
}

/// <summary>Stages self-contained source modules and schema changes. Existing files are never overwritten except the checked authoring definition.</summary>
public static class TemplateObjectImporter
{
    private const long MaximumModuleBytes = 64 * 1024 * 1024;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static async Task<TemplateImportPlan> PrepareAsync(string libraryRoot, string targetProjectRoot,
        TemplateDefinition targetDraft, IEnumerable<TemplateObjectSelection> selections,
        IReadOnlyList<TemplateImportPlan>? previousPlans = null, CancellationToken ct = default)
    {
        targetProjectRoot = TemplatePathGuard.Within(libraryRoot, targetProjectRoot);
        TemplateValidator.ValidateDefinition(targetDraft);
        var selected = selections.Distinct().ToList();
        TemplateValidator.Require(selected.Count is > 0 and <= 200, "Choose between 1 and 200 library objects.");
        previousPlans ??= [];
        TemplateValidator.Require(previousPlans.All(plan => PathComparer.Equals(plan.TargetProjectRoot, targetProjectRoot)), "Import drafts belong to a different Template.");
        var draft = TemplateObjectCloning.Definition(targetDraft);
        var files = new Dictionary<string, TemplateImportFile>(PathComparer);
        var sourceGuards = new Dictionary<string, TemplateSourceFingerprint>(PathComparer);
        var targetGuards = new Dictionary<string, TemplateSourceFingerprint>(PathComparer);
        var selectedAppearance = new Dictionary<TemplateObjectSelection, List<FieldDefinition>>();
        var pending = previousPlans.SelectMany(plan => plan.Files).GroupBy(file => file.RelativePath, PathComparer)
            .ToDictionary(group => group.Key, group => group.First(), PathComparer);
        byte[]? Destination(string relative)
        {
            if (files.TryGetValue(relative, out var newFile)) return newFile.Content;
            if (pending.TryGetValue(relative, out var staged)) return staged.Content;
            var path = TemplatePathGuard.Within(targetProjectRoot, Path.Combine(targetProjectRoot, relative));
            if (!File.Exists(path)) return null;
            var bytes = ReadBounded(path);
            targetGuards[path] = new(path, Hash(bytes));
            return bytes;
        }
        void Stage(string relative, byte[] bytes)
        {
            var existing = Destination(relative);
            if (existing is not null)
            {
                TemplateValidator.Require(existing.AsSpan().SequenceEqual(bytes), $"An imported dependency conflicts with '{relative}'. Save a new Design Group from the edited source, then add it again.");
                return;
            }
            files[relative] = new(relative, bytes, Hash(bytes));
        }
        var layoutCount = 0;
        foreach (var groupSelection in selected.GroupBy(item => item.GroupId))
        {
            ct.ThrowIfCancellationRequested();
            ContentRules.ValidateNodeId(groupSelection.Key);
            var groupRoot = TemplatePathGuard.Within(libraryRoot, Path.Combine(libraryRoot, TemplateObjectLibrary.DirectoryName, "DesignGroups", groupSelection.Key));
            var sourceDefinitionPath = Path.Combine(groupRoot, "Project", "authoring.template.json");
            sourceGuards[sourceDefinitionPath] = new(sourceDefinitionPath, Hash(ReadBounded(sourceDefinitionPath)));
            var metadataPath = Path.Combine(groupRoot, "group.json");
            sourceGuards[metadataPath] = new(metadataPath, Hash(ReadBounded(metadataPath)));
            var group = TemplateObjectLibrary.GetDesignGroup(libraryRoot, groupSelection.Key);
            var objects = TemplateObjectLibrary.ListObjects(libraryRoot, group.Id);
            var items = groupSelection.Select(choice => objects.SingleOrDefault(item => item.Id == choice.ObjectId)
                ?? throw new ContentValidationException("A library object was removed. Refresh the library and choose it again.")).ToList();
            Func<string, string> rewriteAsset = value => value;
            var module = items.Any(item => item.Element is not null || item.Kind is "layout" or "region"
                || item.Appearance.Any(field => field.DefaultValue is { ValueKind: JsonValueKind.String } value && value.GetString()!.Contains("_content/", StringComparison.Ordinal)))
                ? CaptureModule(group.ProjectRoot, targetProjectRoot, Destination, Stage, sourceGuards, out rewriteAsset) : new Dictionary<string, string>(StringComparer.Ordinal);
            JsonElement RewriteValue(JsonElement value) => value.ValueKind == JsonValueKind.String ? ContentJson.Value(rewriteAsset(value.GetString()!)) : value.Clone();
            var sourceDefinition = items[0].Definition;
            var elementMap = new Dictionary<string, string>(StringComparer.Ordinal);
            string AddElement(BlockDefinition source, PageBlock? preset = null)
            {
                var element = TemplateObjectCloning.Element(source);
                TemplateValidator.Require(module.TryGetValue(element.Component.Split('.').Last(), out var component),
                    $"The component '{element.Component}' has no unambiguous local Razor source. Add its .razor component to this Design Group's Pages project before importing.");
                element.Component = component!;
                foreach (var field in element.Fields)
                    if (field.DefaultValue is { } value) field.DefaultValue = RewriteValue(value);
                if (preset is not null)
                    foreach (var field in element.Fields)
                        if (preset.Values.TryGetValue(field.Id, out var value)) field.DefaultValue = RewriteValue(value);
                var equivalent = draft.Blocks.FirstOrDefault(candidate => EquivalentElement(candidate, element));
                if (equivalent is not null) return equivalent.Id;
                element.Id = NextId(preset is null ? source.Id : source.Id + "-" + group.Id[..Math.Min(32, group.Id.Length)], draft.Blocks.Select(item => item.Id));
                if (preset is not null) element.Title = source.Title + " · " + group.Title;
                draft.Blocks.Add(element);
                foreach (var region in draft.Regions.Where(region => region.AllowedBlocks.Count > 0)) region.AllowedBlocks.Add(element.Id);
                return element.Id;
            }
            string EnsureElement(string sourceId)
            {
                if (!elementMap.TryGetValue(sourceId, out var id))
                    elementMap[sourceId] = id = AddElement(sourceDefinition.Blocks.Single(block => block.Id == sourceId));
                return id;
            }
            foreach (var item in items)
            {
                if (item.Kind is "element" or "preset")
                {
                    _ = AddElement(item.Element!, item.Kind == "preset" ? item.Blocks.FirstOrDefault() : null);
                    continue;
                }
                var regionMap = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var sourceRegion in item.Regions)
                {
                    var region = ContentJson.Clone(sourceRegion);
                    if (item.Kind == "layout")
                        region.AllowedBlocks = item.Blocks.Where(block => block.Region == sourceRegion.Id).Select(block => block.Type).Distinct(StringComparer.Ordinal).ToList();
                    else if (region.AllowedBlocks.Count == 0)
                        foreach (var block in item.Definition.Blocks) _ = EnsureElement(block.Id);
                    region.AllowedBlocks = region.AllowedBlocks.Select(EnsureElement).ToList();
                    var equivalent = draft.Regions.FirstOrDefault(existing => existing.Id == region.Id && existing.AllowedBlocks.SequenceEqual(region.AllowedBlocks));
                    if (equivalent is not null) { regionMap[sourceRegion.Id] = equivalent.Id; continue; }
                    region.Id = NextId(region.Id, draft.Regions.Select(existing => existing.Id));
                    draft.Regions.Add(region);
                    regionMap[sourceRegion.Id] = region.Id;
                }
                if (item.Kind == "layout")
                {
                    TemplateValidator.Require(++layoutCount == 1, "Choose one page layout at a time. Other elements, colors, and typography can be combined freely.");
                    draft.DefaultBlocks = item.Blocks.Select(block =>
                    {
                        var copy = ContentJson.Clone(block);
                        copy.Type = EnsureElement(block.Type);
                        copy.Region = regionMap.GetValueOrDefault(block.Region, draft.Regions[0].Id);
                        copy.Values = copy.Values.ToDictionary(pair => pair.Key, pair => RewriteValue(pair.Value), StringComparer.Ordinal);
                        return copy;
                    }).ToList();
                    draft.EmptyPageDefault = draft.DefaultBlocks.Count == 0;
                }
                var appearance = new List<FieldDefinition>();
                foreach (var sourceField in item.Appearance)
                {
                    var field = TemplateObjectCloning.Field(sourceField);
                    if (field.DefaultValue is { } value) field.DefaultValue = RewriteValue(value);
                    appearance.Add(field);
                }
                selectedAppearance[new(item.GroupId, item.Id)] = appearance;
            }
        }
        // Source modules are grouped for capture, but overlapping styles follow the user's collection order.
        foreach (var selection in selected)
            if (selectedAppearance.TryGetValue(selection, out var fields))
                foreach (var field in fields)
                {
                    var index = draft.Appearance.FindIndex(existing => existing.Id == field.Id);
                    if (index < 0) draft.Appearance.Add(field);
                    else
                    {
                        // A source design may have been saved before newer choices were introduced.
                        // Keep its selected default while retaining options that existing Pages still use.
                        var existing = draft.Appearance[index];
                        if (field.Type == "select" && existing.Type == "select")
                            field.Options = field.Options.Concat(existing.Options).Distinct(StringComparer.Ordinal).ToList();
                        draft.Appearance[index] = field;
                    }
                }
        TemplateValidator.ValidateDefinition(draft);
        // A plan is a snapshot. Recheck sources after all reads before showing the draft to the user.
        CheckFingerprints(sourceGuards.Values, "A source Design Group changed. Refresh it and add the objects again.");
        await Task.CompletedTask;
        return new() { TargetProjectRoot = targetProjectRoot, Definition = draft, Files = files.Values.ToList(), SourceFiles = sourceGuards.Values.ToList(),
            TargetFiles = targetGuards.Values.ToList(), Summary = $"{selected.Count} object{(selected.Count == 1 ? "" : "s")} added to the Template draft." };
    }

    public static async Task CommitAsync(string targetProjectRoot, TemplateDefinition finalEditedDefinition,
        IReadOnlyList<TemplateImportPlan> plans, string expectedDefinitionRawSha256, CancellationToken ct = default)
    {
        targetProjectRoot = Path.GetFullPath(targetProjectRoot);
        TemplatePathGuard.NoLinks(targetProjectRoot);
        TemplateValidator.ValidateDefinition(finalEditedDefinition);
        TemplateValidator.Require(plans.All(plan => PathComparer.Equals(plan.TargetProjectRoot, targetProjectRoot)), "Import drafts belong to a different Template.");
        var gate = Gates.GetOrAdd(targetProjectRoot, _ => new(1, 1));
        await gate.WaitAsync(ct);
        var created = new List<(string Path, string Hash)>();
        try
        {
            var definitionPath = TemplatePathGuard.Within(targetProjectRoot, Path.Combine(targetProjectRoot, "authoring.template.json"));
            var definitionGuard = new TemplateSourceFingerprint(definitionPath, expectedDefinitionRawSha256);
            CheckFingerprints([definitionGuard], "The Template changed on disk. Reload it before saving these imports.");
            CheckFingerprints(plans.SelectMany(plan => plan.SourceFiles), "A source Design Group changed. Refresh it and add the objects again.");
            CheckFingerprints(plans.SelectMany(plan => plan.TargetFiles), "A destination dependency changed. Reload the Template before saving these imports.");
            var allFiles = plans.SelectMany(plan => plan.Files).GroupBy(file => file.RelativePath, PathComparer).Select(group =>
            {
                TemplateValidator.Require(group.All(file => file.Sha256 == group.First().Sha256), "Two staged objects have conflicting dependencies.");
                return group.First();
            }).ToList();
            foreach (var file in allFiles)
            {
                TemplateValidator.Require(Hash(file.Content) == file.Sha256, "An imported dependency changed after it was staged.");
                var path = TemplatePathGuard.Within(targetProjectRoot, Path.Combine(targetProjectRoot, file.RelativePath));
                TemplateValidator.Require(!File.Exists(path) || Hash(ReadBounded(path)) == file.Sha256, "An imported destination file already exists with different contents. Reload the Template.");
            }
            foreach (var file in allFiles)
            {
                ct.ThrowIfCancellationRequested();
                var path = TemplatePathGuard.Within(targetProjectRoot, Path.Combine(targetProjectRoot, file.RelativePath));
                if (File.Exists(path)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".import";
                try
                {
                    await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
                    {
                        await stream.WriteAsync(file.Content, ct);
                        await stream.FlushAsync(ct);
                        stream.Flush(true);
                    }
                    ct.ThrowIfCancellationRequested();
                    // Atomic add protects a concurrent editor/import and never publishes a partial dependency.
                    File.Move(temporary, path, overwrite: false);
                    created.Add((path, file.Sha256));
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            CheckFingerprints([definitionGuard], "The Template changed while imports were being saved. Reload it and try again.");
            CheckFingerprints(plans.SelectMany(plan => plan.SourceFiles), "A source Design Group changed while imports were being saved.");
            CheckFingerprints(plans.SelectMany(plan => plan.TargetFiles), "A destination dependency changed while imports were being saved.");
            await TemplatePathGuard.ReplaceAsync(definitionPath, JsonSerializer.SerializeToUtf8Bytes(finalEditedDefinition, ContentJson.Options), ct);
        }
        catch
        {
            foreach (var item in created.AsEnumerable().Reverse())
                try { if (File.Exists(item.Path) && Hash(ReadBounded(item.Path)) == item.Hash) File.Delete(item.Path); } catch (IOException) { }
            throw;
        }
        finally { gate.Release(); }
    }

    public static async Task<ReusableTemplateInfo> CreateTemplateFromObjectsAsync(string libraryRoot, string title,
        IEnumerable<TemplateObjectSelection> selections, CancellationToken ct = default)
    {
        ContentRules.ValidateTitle(title);
        var chosen = selections.ToList();
        var id = "custom-" + Guid.NewGuid().ToString("N");
        var parent = TemplatePathGuard.Within(libraryRoot, Path.Combine(libraryRoot, "Templates"));
        var stage = TemplatePathGuard.Within(libraryRoot, Path.Combine(parent, ".pending-" + Guid.NewGuid().ToString("N")));
        var project = Path.Combine(stage, "Project");
        await TemplateProjectScaffolder.EnsureAsync(libraryRoot, project, "template-" + id, title.Trim(), ct, ReusableTemplateCatalog.BlankId, exportingCatalog: true);
        var definition = TemplateObjectLibrary.ReadDefinition(project);
        definition.Title = title.Trim();
        definition.Blocks = [definition.Blocks.Single(block => block.Id == "text")];
        definition.Regions = [new() { Id = "main", Title = "Page content" }];
        if (chosen.Count > 0)
        {
            var plan = await PrepareAsync(libraryRoot, project, definition, chosen, ct: ct);
            definition = plan.Definition;
            if (definition.Blocks.Count > 1 && !definition.DefaultBlocks.Any(block => block.Type == "text")
                && !chosen.Any(choice => TemplateObjectLibrary.ListObjects(libraryRoot, choice.GroupId).Any(item => item.Id == choice.ObjectId && item.Element?.Id == "text")))
            {
                definition.Blocks.RemoveAll(block => block.Id == "text");
                foreach (var region in definition.Regions) region.AllowedBlocks.Remove("text");
            }
            await CommitAsync(project, definition, [plan], Hash(ReadBounded(Path.Combine(project, "authoring.template.json"))), ct);
        }
        else await TemplatePathGuard.ReplaceAsync(Path.Combine(project, "authoring.template.json"), JsonSerializer.SerializeToUtf8Bytes(definition, ContentJson.Options), ct);
        var item = new ReusableTemplateInfo(id, title.Trim(), "A modular Template assembled from the Core Library.", "My templates", false);
        await TemplatePathGuard.WriteNewAsync(Path.Combine(stage, "template.json"), JsonSerializer.SerializeToUtf8Bytes(item, ContentJson.Options), ct);
        ct.ThrowIfCancellationRequested();
        Directory.Move(stage, TemplatePathGuard.Within(libraryRoot, Path.Combine(parent, id)));
        return item;
    }

    private static Dictionary<string, string> CaptureModule(string sourceRoot, string targetRoot,
        Func<string, byte[]?> destination, Action<string, byte[]> stage,
        Dictionary<string, TemplateSourceFingerprint> guards, out Func<string, string> rewriteAsset)
    {
        rewriteAsset = value => value;
        var pages = TemplatePathGuard.Within(sourceRoot, Path.Combine(sourceRoot, "src", "Pages"));
        TemplateValidator.Require(Directory.Exists(pages), "This Design Group has no local component source. Open its project and add the source before importing.");
        guards[pages] = new(pages, DirectoryHash(pages));
        var sourceProject = Path.Combine(pages, "Pages.csproj");
        var projectBytes = ReadBounded(sourceProject);
        guards[sourceProject] = new(sourceProject, Hash(projectBytes));
        var sourceXml = XDocument.Parse(Encoding.UTF8.GetString(projectBytes));
        var targetProjectBytes = destination("src/Pages/Pages.csproj") ?? throw new ContentValidationException("The destination has no Pages project.");
        var targetXml = XDocument.Parse(Encoding.UTF8.GetString(targetProjectBytes));
        static IEnumerable<string> ExtraReferences(XDocument xml) => xml.Descendants().Where(element => element.Name.LocalName is "PackageReference" or "Reference")
            .Select(element => element.ToString(SaveOptions.DisableFormatting)).Order(StringComparer.Ordinal);
        TemplateValidator.Require(ExtraReferences(sourceXml).All(reference => ExtraReferences(targetXml).Contains(reference)),
            "This object needs a package or assembly reference that the destination does not have. Add that dependency to the destination project, then import again.");
        TemplateValidator.Require(sourceXml.Descendants("ProjectReference").All(reference => reference.Attribute("Include")?.Value.EndsWith("HyperLib.Runtime.csproj", StringComparison.OrdinalIgnoreCase) == true),
            "This object uses an additional project dependency. Add its source to the Template's Pages project before publishing the Design Group.");
        var sourceNamespace = sourceXml.Descendants("RootNamespace").FirstOrDefault()?.Value ?? "HyperLib.Work";
        var sourceAssembly = sourceXml.Descendants("AssemblyName").FirstOrDefault()?.Value ?? "HyperLib.Work";
        var targetAssembly = targetXml.Descendants("AssemblyName").FirstOrDefault()?.Value ?? "HyperLib.Work";
        var captured = TemplatePathGuard.Files(pages).Where(path =>
        {
            var relative = Path.GetRelativePath(pages, path).Replace('\\', '/');
            return !relative.StartsWith("Generated/", StringComparison.OrdinalIgnoreCase) && !relative.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                && !relative.EndsWith(".csproj.user", StringComparison.OrdinalIgnoreCase);
        }).Order(StringComparer.Ordinal).ToDictionary(path => Path.GetRelativePath(pages, path).Replace('\\', '/'), path =>
        {
            var bytes = ReadBounded(path);
            guards[path] = new(path, Hash(bytes));
            return bytes;
        }, PathComparer);
        TemplateValidator.Require(captured.Count <= 2000 && captured.Values.Sum(bytes => (long)bytes.Length) <= MaximumModuleBytes,
            "The component module exceeds the import limit of 2,000 files or 64 MiB. Split it into smaller Design Groups.");
        var identity = Hash(Encoding.UTF8.GetBytes(string.Join('\n', captured.Select(pair => pair.Key + ":" + Hash(pair.Value)))))[..12].ToLowerInvariant();
        var prefix = "Hl" + identity + "_";
        var moduleName = "G" + identity;
        var importedNamespace = sourceNamespace + ".Imports." + moduleName;
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var copiedComponents = new HashSet<string>(StringComparer.Ordinal);
        // Reuse is permitted only for a byte-identical complete module. A helper or JS change can
        // alter an otherwise identical Razor component; isolate the whole module in that case.
        var identicalModule = captured.All(pair => destination("src/Pages/" + pair.Key) is { } bytes && bytes.AsSpan().SequenceEqual(pair.Value));
        foreach (var file in captured.Where(pair => pair.Key.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) && !pair.Key.EndsWith("_Imports.razor", StringComparison.Ordinal)))
        {
            var name = Path.GetFileNameWithoutExtension(file.Key);
            TemplateValidator.Require(names.Add(name), "This source module contains ambiguous component names. Give its components unique names before importing.");
            if (identicalModule)
                aliases[name] = name;
            else { aliases[name] = prefix + name; copiedComponents.Add(name); }
        }
        if (copiedComponents.Count == 0) return aliases;
        string RewriteAsset(string value) => value.Replace("_content/" + sourceAssembly + "/", "_content/" + targetAssembly + "/hyperlib-imports/" + identity + "/", StringComparison.Ordinal);
        rewriteAsset = RewriteAsset;
        // All local helpers and assets travel with this bounded module; no host or project file is imported.
        string Rewrite(string text)
        {
            text = Regex.Replace(text, @"(?<![A-Za-z0-9_])" + Regex.Escape(sourceNamespace) + @"(?=\.|\s|;|$)", importedNamespace, RegexOptions.CultureInvariant);
            foreach (var alias in aliases.Where(pair => pair.Key != pair.Value).OrderByDescending(pair => pair.Key.Length))
                text = Regex.Replace(text, @"\b" + Regex.Escape(alias.Key) + @"\b", alias.Value, RegexOptions.CultureInvariant);
            return RewriteAsset(text);
        }
        var globalImports = new StringBuilder();
        if (captured.TryGetValue("_Imports.razor", out var imports)) globalImports.AppendLine(Rewrite(Encoding.UTF8.GetString(imports)));
        foreach (var parent in captured.Keys.Where(path => path.EndsWith(".razor", StringComparison.Ordinal) && !path.EndsWith("_Imports.razor", StringComparison.Ordinal))
            .Select(path => Path.GetDirectoryName(path)?.Replace('\\', '.').Replace('/', '.') ?? "").Distinct())
        {
            globalImports.AppendLine("@using " + importedNamespace + (parent.Length == 0 ? "" : "." + parent));
        }
        stage("src/Pages/Imported/" + moduleName + "/_Imports.razor", Encoding.UTF8.GetBytes(globalImports.ToString()));
        foreach (var (relative, bytes) in captured)
        {
            if (relative == "_Imports.razor") continue;
            var isStatic = relative.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase);
            var componentName = Path.GetFileName(relative).Split('.')[0];
            if (!isStatic && aliases.ContainsKey(componentName) && !copiedComponents.Contains(componentName)) continue;
            var targetRelative = isStatic ? "src/Pages/wwwroot/hyperlib-imports/" + identity + "/" + relative[8..]
                : "src/Pages/Imported/" + moduleName + "/" + relative;
            if (!isStatic && aliases.TryGetValue(componentName, out var alias))
                targetRelative = targetRelative[..(targetRelative.Length - Path.GetFileName(relative).Length)] + alias + Path.GetFileName(relative)[componentName.Length..];
            var extension = Path.GetExtension(relative).ToLowerInvariant();
            if (extension is ".razor" or ".cs" or ".css" or ".js" or ".json" or ".html")
            {
                var text = Rewrite(Encoding.UTF8.GetString(bytes));
                if (extension == ".razor" && !relative.EndsWith("_Imports.razor", StringComparison.Ordinal))
                {
                    var parent = Path.GetDirectoryName(relative)?.Replace('\\', '.').Replace('/', '.') ?? "";
                    // Razor's generated namespace must be explicit after moving the source folder.
                    if (!Regex.IsMatch(text, @"(?m)^\s*@namespace\s+")) text = "@namespace " + importedNamespace + (parent.Length == 0 ? "" : "." + parent) + "\n" + text;
                }
                stage(targetRelative, Encoding.UTF8.GetBytes(text));
            }
            else stage(targetRelative, bytes);
        }
        return aliases;
    }

    private static bool EquivalentElement(BlockDefinition left, BlockDefinition right)
    {
        var copy = TemplateObjectCloning.Element(right); copy.Id = left.Id; copy.Title = left.Title;
        return JsonSerializer.Serialize(left, ContentJson.Options) == JsonSerializer.Serialize(copy, ContentJson.Options);
    }
    private static string NextId(string basis, IEnumerable<string> existing)
    {
        var used = existing.ToHashSet(StringComparer.Ordinal);
        var stem = basis[..Math.Min(88, basis.Length)];
        if (!used.Contains(stem)) return stem;
        for (var number = 2; ; number++) if (!used.Contains(stem + "-" + number)) return stem + "-" + number;
    }
    private static byte[] ReadBounded(string path)
    {
        TemplatePathGuard.NoLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        TemplateValidator.Require(stream.Length <= MaximumModuleBytes, "A component dependency exceeds the 64 MiB import limit.");
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string DirectoryHash(string path)
        => Hash(Encoding.UTF8.GetBytes(string.Join('\n', TemplatePathGuard.Files(path).Order(StringComparer.Ordinal)
            .Select(file => Path.GetRelativePath(path, file) + ":" + Hash(ReadBounded(file))))));
    private static void CheckFingerprints(IEnumerable<TemplateSourceFingerprint> values, string message)
    {
        foreach (var value in values)
            TemplateValidator.Require((Directory.Exists(value.Path) ? DirectoryHash(value.Path)
                : File.Exists(value.Path) ? Hash(ReadBounded(value.Path)) : "").Equals(value.Sha256, StringComparison.OrdinalIgnoreCase), message);
    }
}
