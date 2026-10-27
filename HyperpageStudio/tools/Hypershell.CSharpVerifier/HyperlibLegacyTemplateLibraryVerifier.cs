using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Services;

public static class HyperlibLegacyTemplateLibraryVerifier
{
    public static async Task VerifyAsync(string outputRoot, string sourceLibraryRoot)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceLibraryRoot));
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        if (source.Equals(output, StringComparison.OrdinalIgnoreCase) || output.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Legacy conversion checks require a separate fixture directory.");
        var root = Path.Combine(output, "legacy-" + Guid.NewGuid().ToString("N"));
        var library = Path.Combine(root, "library");
        Directory.CreateDirectory(library);
        CopyTree(Path.Combine(source, "Starters", "standard"), Path.Combine(library, "Starters", "standard"));
        CopyTree(Path.Combine(source, "src", "HyperLib.Core"), Path.Combine(library, "src", "HyperLib.Core"));
        CopyTree(Path.Combine(source, "src", "HyperLib.Runtime"), Path.Combine(library, "src", "HyperLib.Runtime"));
        var assertions = new List<string>();
        void Check(bool passed, string description) { if (!passed) throw new InvalidOperationException(description); assertions.Add(description); }
        var modules = HyperlibLegacyTemplateLibraryService.CreateModules();
        var other = HyperlibLegacyTemplateLibraryService.CreateModules();
        Check(modules.Count == 58 && modules.Select(module => module.SourceTemplateId).Distinct().Count() == 58, "All 58 existing Hyperpage templates become distinct portable modules.");
        Check(modules.Zip(other).All(pair => pair.First.Razor == pair.Second.Razor && pair.First.Css == pair.Second.Css), "Legacy source conversion produces deterministic markup and scoped CSS.");
        Check(modules.Select(module => module.Definition.CatalogId).Distinct().Count() == 58 && modules.All(module => module.Definition.SourceTemplateId == module.SourceTemplateId), "Every legacy module has a unique stable ID and original Template provenance.");
        Check(modules.All(module => module.Definition.Fields.Count >= 4 && module.Definition.Fields.Count <= 200), "Ported modules expose bounded editable fields and element styles.");
        Check(modules.All(module => !module.Razor.Contains("@page ") && !module.Razor.Contains("<link rel=\"stylesheet\"")), "Portable snippets do not retain competing routes or global theme links.");
        Check(modules.Where(module => module.Razor.Contains("<form")).All(module => module.Razor.Contains("@onsubmit:preventDefault")), "Forms remain local demonstrations until the Work adds a handler.");
        var groups = await HyperlibLegacyTemplateLibraryService.EnsureLegacyDesignGroupsAsync(library);
        Check(groups.Count == 3, "The complete legacy catalog is organized into its three original libraries.");
        var importedObjects = groups.SelectMany(group => TemplateObjectLibrary.ListObjects(library, group.Id)).Where(item => item.Kind == "element").ToArray();
        Check(importedObjects.Length == 58 && importedObjects.All(item => item.CatalogId.StartsWith("HLC-LEGACY-")), "The normal Core Library exposes every legacy port using stable lookup IDs.");
        foreach (var group in groups)
        {
            var definition = TemplateObjectLibrary.ReadDefinition(group.ProjectRoot);
            TemplateValidator.ValidateDefinition(definition);
            Check(definition.Blocks.All(block => File.Exists(Path.Combine(group.ProjectRoot, "src/Pages/Blocks", block.Component + ".razor"))
                && File.Exists(Path.Combine(group.ProjectRoot, "src/Pages/Blocks", block.Component + ".razor.css"))), group.Title + " owns all component source and scoped styles.");
            Check(File.Exists(Path.Combine(group.ProjectRoot, "src/Pages/wwwroot/legacy/placeholder.svg")), group.Title + " includes a local preview image.");
            var build = await BuildPagesAsync(Path.Combine(group.ProjectRoot, "src/Pages/Pages.csproj"), Path.Combine(root, "artifacts", group.Id));
            await File.WriteAllTextAsync(Path.Combine(root, group.Id + "-build.log"), build.Output);
            Check(build.ExitCode == 0, group.Title + " compiles every actual Razor component and scoped stylesheet.");
        }
        var before = Fingerprint(Path.Combine(library, "CoreLibrary"));
        await HyperlibLegacyTemplateLibraryService.EnsureLegacyDesignGroupsAsync(library);
        Check(Fingerprint(Path.Combine(library, "CoreLibrary")) == before, "Repeated seed installation preserves published Design Groups byte for byte.");
        var target = await ReusableTemplateManager.CreateBlankAsync(library, "Legacy import verification");
        var targetRoot = ReusableTemplateCatalog.GetProjectRoot(library, target.Id);
        var donor = importedObjects.Single(item => item.SourceTemplateId == "atlas.archive.element.quote.v1");
        var plan = await TemplateObjectImporter.PrepareAsync(library, targetRoot, TemplateObjectLibrary.ReadDefinition(targetRoot), [new(donor.GroupId, donor.Id)]);
        Check(plan.Definition.Blocks.Any(block => block.CatalogId == donor.CatalogId) && plan.Files.Any(file => file.RelativePath.EndsWith(".razor.css")), "A real legacy object imports with its stable identity and required styles.");
        var snapshot = Path.Combine(targetRoot, "authoring.template.json");
        await TemplateObjectImporter.CommitAsync(targetRoot, plan.Definition, [plan], Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(snapshot))));
        var importedBuild = await BuildPagesAsync(Path.Combine(targetRoot, "src/Pages/Pages.csproj"), Path.Combine(root, "artifacts", "imported-legacy"));
        await File.WriteAllTextAsync(Path.Combine(root, "imported-legacy-build.log"), importedBuild.Output);
        Check(importedBuild.ExitCode == 0, "A legacy object and its complete imported dependency module compile in another Template.");
        await File.WriteAllTextAsync(Path.Combine(root, "legacy-verification.json"), JsonSerializer.Serialize(new { passed = true, assertions, modules = modules.Count, groups = groups.Count, root }, ContentJson.Options));
        Console.WriteLine($"HYPERLIB_LEGACY_LIBRARY_OK assertions={assertions.Count} modules={modules.Count} groups={groups.Count} root={root}");
    }

    private static async Task<(int ExitCode, string Output)> BuildPagesAsync(string project, string artifacts)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in new[] { "build", project, "-c", "Release", "--artifacts-path", artifacts, "--nologo", "-v:minimal" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the isolated Razor build.");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdout + await stderr);
    }
    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source).Where(path => Path.GetFileName(path) is not ("bin" or "obj" or ".git")))
            CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
    private static string Fingerprint(string root)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(root, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))))));
}
