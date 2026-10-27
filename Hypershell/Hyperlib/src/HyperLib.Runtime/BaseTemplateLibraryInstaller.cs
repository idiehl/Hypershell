using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using HyperLib.Core;

namespace HyperLib.Runtime;

/// <summary>Ships reusable starter projects and explicitly installs the reversible demonstration collection.</summary>
public static class BaseTemplateLibraryInstaller
{
    public const string CollectionId = "base-template-library";
    private const string Version = "base-template-library-v1";

    public static async Task EnsureCatalogAsync(string root, CancellationToken ct = default)
    {
        var catalogRoot = Path.Combine(root, "BaseTemplates");
        LibraryPathGuard.Within(root, catalogRoot);
        Directory.CreateDirectory(catalogRoot);
        await WriteNewAsync(Path.Combine(catalogRoot, "README.md"),
            "# Base Template Library\n\nSeventeen reusable starters: the two original designs and fifteen additional designs. In HyperLib, create a Work and choose a starter. Every new work receives its own template, components, theme, and default page content. Add Page reuses that work's defaults.\n\nEach folder includes reference snapshots authoring.template.json and default-page.json, plus a complete Studio Project. Project/authoring.template.json is the canonical reusable schema used for new Works; the top-level JSON files document the shipped design. Open Project/visual-page.project.json in Hyperpage Studio to edit the reusable source. The projects use the HyperLib runtime SDK under this library root. Existing works do not track edits to these source packages.\n\nThe Base Template Library collection contains the fifteen additional design demos. Its installation is one reversible content batch. Forms show a local confirmation without sending or storing entries; maps and web embeds load only when activated.\n", ct);
        await WriteNewAsync(Path.Combine(catalogRoot, "catalog.json"), JsonSerializer.Serialize(BaseTemplateCatalog.List(), ContentJson.Options), ct);
        foreach (var item in BaseTemplateCatalog.List())
        {
            var entryRoot = Path.Combine(catalogRoot, item.Id);
            var template = BaseTemplateCatalog.CreateTemplate(item.Id);
            await WriteNewAsync(Path.Combine(entryRoot, "authoring.template.json"), JsonSerializer.Serialize(template, ContentJson.Options), ct);
            await WriteNewAsync(Path.Combine(entryRoot, "default-page.json"), JsonSerializer.Serialize(BaseTemplateCatalog.CreateDemoPage(item.Id, "example-page", "example-work"), ContentJson.Options), ct);
            await WriteNewAsync(Path.Combine(entryRoot, "README.md"), $"# {item.Title}\n\n{item.Description}\n\nCategory: {item.Category}.\n\nCreate a Work in HyperLib and select this starter. The Project folder is a permanent, independently editable Hyperpage Studio project. Its authoring.template.json is the canonical schema copied into new Works; the top-level JSON files are shipped reference snapshots. It references the HyperLib runtime SDK shipped in this library.\n", ct);
            await ProjectScaffolder.EnsureAsync(root, Path.Combine(entryRoot, "Project"), "template-" + item.Id, item.Title, ct, item.Id, exportingCatalog: true);
            if (!item.IsOriginal)
                await WriteNewAsync(Path.Combine(root, "Media", "base-templates", item.Id + ".svg"), Illustration(item), ct);
        }
    }

    public static async Task<LibraryState> InstallAsync(string root, ContentStore store, CancellationToken ct = default)
    {
        await EnsureCatalogAsync(root, ct);
        var marker = Path.Combine(root, ".hyperlib", Version + ".json");
        LibraryPathGuard.Within(root, marker);
        if (File.Exists(marker)) return await store.ReadAsync(ct);
        var state = await store.ReadAsync(ct);
        var newDesigns = BaseTemplateCatalog.List().Where(t => !t.IsOriginal).ToArray();
        if (state.Nodes.Any(n => n.Id == CollectionId && n.Type == "collection") && newDesigns.All(item =>
            state.Nodes.Any(n => n.Id == "demo-" + item.Id && n.Type == "work" && n.ParentId == CollectionId && n.StarterId == item.Id)
            && state.Pages.TryGetValue("demo-" + item.Id + "-page", out var page) && page.WorkId == "demo-" + item.Id))
        {
            // A commit is authoritative even if a crash interrupted the following marker write.
            await WriteNewAsync(marker, JsonSerializer.Serialize(new { version = Version, installedRevision = state.Revision, recovered = true }, ContentJson.Options), ct);
            return state;
        }
        // Additive schema upgrades retain every existing component, field, setting and content block.
        await UpgradeTemplateAsync(root, root, "library", null, ct);
        await UpgradeStockPresentationAsync(root, root, "library", ct);
        foreach (var work in state.Nodes.Where(n => n.Type == "work"))
        {
            var starterId = work.StarterId ?? (work.Id == "wave-lab" ? "wave-laboratory" : "woodland-editorial");
            var project = Path.Combine(root, "Works", work.Id);
            await ProjectScaffolder.EnsureAsync(root, project, work.Id, work.Title, ct, starterId);
            await UpgradeStockPresentationAsync(root, project, work.Id, ct);
            await UpgradeTemplateAsync(root, project, work.Id, starterId, ct);
        }
        store.TemplateResolver = id => ReadTemplate(root, id);
        var operations = new List<ContentOperation>();
        if (state.Nodes.All(n => n.Id != CollectionId))
            operations.Add(new() { Kind = "createNode", Id = CollectionId, NodeType = "collection", ParentId = state.LibraryId, Title = "Base Template Library" });
        else if (state.Nodes.Single(n => n.Id == CollectionId).Type != "collection")
            throw new ContentValidationException("The reserved Base Template Library identity belongs to a different item; rename its identity before installing demos.");
        foreach (var item in BaseTemplateCatalog.List().Where(t => !t.IsOriginal))
        {
            var workId = "demo-" + item.Id;
            var pageId = workId + "-page";
            if (state.Nodes.Any(n => n.Id == workId || n.Id == pageId))
                throw new ContentValidationException($"A demonstration identity already exists: {workId}. Existing content was preserved.");
            operations.Add(new() { Kind = "createNode", Id = workId, NodeType = "work", ParentId = CollectionId, Title = item.Title, StarterId = item.Id,
                Cover = "/media/base-templates/" + item.Id + ".svg" });
            // Resolve edited reusable appearance/schema/defaults together inside the store batch.
            operations.Add(new() { Kind = "createNode", Id = pageId, NodeType = "page", ParentId = workId, Title = item.DemoTitle });
        }
        foreach (var existing in state.Pages.Values)
        {
            var template = ReadTemplate(root, existing.WorkId);
            if (template is null || template.DefaultBlocks.Count == 0) continue;
            var updated = ContentJson.Clone(existing);
            updated.UsesEditableChrome = true;
            if (!updated.Blocks.Any(b => b.Type is "header" or "banner") && Allows(template, "header"))
                updated.Blocks.Insert(0, new() { Id = "header-" + Guid.NewGuid().ToString("N"), Type = "header", Region = "main",
                    Values = new() { ["eyebrow"] = ContentJson.Value(state.Nodes.Single(n => n.Id == existing.WorkId).Title), ["title"] = ContentJson.Value(existing.Title), ["text"] = ContentJson.Value("") } });
            if (!updated.Blocks.Any(b => b.Type == "footer") && Allows(template, "footer"))
                updated.Blocks.Add(new() { Id = "footer-" + Guid.NewGuid().ToString("N"), Type = "footer", Region = "main",
                    Values = new() { ["text"] = ContentJson.Value(state.Nodes.Single(n => n.Id == existing.WorkId).Title) } });
            if (updated.Blocks.Count != existing.Blocks.Count || updated.UsesEditableChrome != existing.UsesEditableChrome)
                operations.Add(new() { Kind = "savePage", Id = existing.Id, Page = updated });
        }
        var installed = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Install Base Template Library and editable page headings", Operations = operations }, ct);
        await WriteNewAsync(marker, JsonSerializer.Serialize(new { version = Version, installedRevision = installed.Revision, templates = 17, demoWorks = 15, installedAt = DateTimeOffset.UtcNow }, ContentJson.Options), ct);
        return installed;
    }

    private static bool Allows(TemplateDefinition template, string type) => template.Blocks.Any(b => b.Id == type) &&
        template.Regions.Any(r => r.Id == "main" && (r.AllowedBlocks.Count == 0 || r.AllowedBlocks.Contains(type)));

    private static TemplateDefinition? ReadTemplate(string root, string id)
    {
        var file = id == "library" ? Path.Combine(root, "Presentation", "authoring.template.json") : Path.Combine(root, "Works", id, "authoring.template.json");
        LibraryPathGuard.Within(root, file);
        return File.Exists(file) ? JsonSerializer.Deserialize<TemplateDefinition>(File.ReadAllText(file), ContentJson.Options) : null;
    }

    private static async Task UpgradeTemplateAsync(string root, string project, string id, string? starterId, CancellationToken ct)
    {
        // The legacy demo migration only upgrades shipped legacy designs, never a user's blank or custom contract.
        if (starterId is not null && !BaseTemplateCatalog.Contains(starterId)) return;
        var file = Path.Combine(project, id == "library" ? "Presentation/authoring.template.json" : "authoring.template.json");
        LibraryPathGuard.Within(root, file);
        if (!File.Exists(file)) return;
        var original = await File.ReadAllTextAsync(file, ct);
        var template = JsonSerializer.Deserialize<TemplateDefinition>(original, ContentJson.Options)!;
        if (template.TemplateId != "standard") return;
        var extension = starterId is null ? StarterTemplate.Create() : BaseTemplateCatalog.CreateTemplate(starterId);
        foreach (var definition in extension.Blocks)
        {
            // This explicit legacy migration also installs newly supported stock components.
            // Ordinary startup leaves every completed Work independent of its original starter.
            var relative = Path.Combine("src", "Pages", "Blocks", definition.Component.Split('.').Last() + ".razor");
            var shipped = LibraryPathGuard.Within(root, Path.Combine(root, "Starters", "standard", relative));
            if (File.Exists(shipped))
                await LibraryPathGuard.WriteNewAsync(LibraryPathGuard.Within(root, Path.Combine(project, relative)), await File.ReadAllBytesAsync(shipped, ct), ct);
            var existing = template.Blocks.FirstOrDefault(b => b.Id == definition.Id);
            if (existing is null) template.Blocks.Add(ContentJson.Clone(definition));
            else if (existing.Component == definition.Component)
                MergeFields(existing.Fields, definition.Fields);
        }
        foreach (var region in template.Regions.Where(r => r.Id == "main" && r.AllowedBlocks.Count > 0))
            foreach (var block in extension.Blocks)
                if (!region.AllowedBlocks.Contains(block.Id)) region.AllowedBlocks.Add(block.Id);
        MergeFields(template.Appearance, extension.Appearance);
        if (starterId is not null && template.DefaultBlocks.Count == 0)
            template.DefaultBlocks = ContentJson.Clone(extension.DefaultBlocks);
        TemplateValidator.ValidateDefinition(template);
        var updated = JsonSerializer.Serialize(template, ContentJson.Options);
        if (updated == original) return;
        var backup = Path.Combine(root, ".hyperlib", "recovery", Version, id + ".authoring.template.json");
        await WriteNewAsync(backup, original, ct);
        await WriteAtomicAsync(file, updated, ct);
    }

    private static void MergeFields(List<FieldDefinition> destination, IEnumerable<FieldDefinition> additions)
    {
        foreach (var field in additions)
        {
            var previous = destination.FirstOrDefault(f => f.Id == field.Id);
            if (previous is null) destination.Add(ContentJson.Clone(field));
            else if (previous.Type == "select" && field.Type == "select")
                foreach (var option in field.Options) if (!previous.Options.Contains(option)) previous.Options.Add(option);
        }
    }

    private static async Task UpgradeStockPresentationAsync(string root, string project, string id, CancellationToken ct)
    {
        var stock = new Dictionary<string, string>
        {
            ["GalleryBlock.razor"] = "010070117F30035A715AE68FC22E9C64C167A261FF73E7AE74FF2826BC68E113",
            ["VideoBlock.razor"] = "2AC1E87FB850BEEFDC7CD064825BFF00A6D2C774930B5ED088732385669A0EFF"
        };
        foreach (var entry in stock)
        {
            var relative = Path.Combine("src", "Pages", "Blocks", entry.Key);
            var path = Path.Combine(project, relative);
            if (!File.Exists(path) || Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, ct))) != entry.Value) continue;
            await WriteNewAsync(Path.Combine(root, ".hyperlib", "recovery", Version, id, relative), await File.ReadAllTextAsync(path, ct), ct);
            await WriteAtomicAsync(path, await File.ReadAllTextAsync(Path.Combine(root, "Starters", "standard", relative), ct), ct);
        }
        var shellRelative = "src/Pages/Generated/WorkPage.razor";
        var shellPath = Path.Combine(project, shellRelative);
        if (!File.Exists(shellPath)) return;
        var oldShell = await File.ReadAllTextAsync(shellPath, ct);
        const string known = "@namespace HyperLib.Work.Generated\n@page \"/presentation\"\n<div class=\"hyperlib-studio-shell\"><p class=\"hl-kicker\">HYPERLIB</p></div>";
        if (oldShell.Replace("\r\n", "\n").Trim() != known) return;
        var updated = oldShell.Replace("<p class=\"hl-kicker\">HYPERLIB</p>", "", StringComparison.Ordinal);
        await WriteNewAsync(Path.Combine(root, ".hyperlib", "recovery", Version, id, shellRelative), oldShell, ct);
        await WriteAtomicAsync(shellPath, updated, ct);
        var documentPath = Path.Combine(project, "Pages/presentation.visual-page.json");
        if (!File.Exists(documentPath)) return;
        var oldDocument = await File.ReadAllTextAsync(documentPath, ct);
        var document = JsonNode.Parse(oldDocument)!;
        if (document["root"]?["children"] is JsonArray children && children.Count == 1 && children[0]?["text"]?.GetValue<string>() == "HYPERLIB")
        {
            children.Clear();
            if (document["generatedFiles"] is JsonArray files)
                foreach (var file in files)
                    if (file?["relativePath"]?.GetValue<string>() == shellRelative)
                        file["sha256"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(updated)));
            await WriteNewAsync(Path.Combine(root, ".hyperlib", "recovery", Version, id, "Pages/presentation.visual-page.json"), oldDocument, ct);
            await WriteAtomicAsync(documentPath, document.ToJsonString(ContentJson.Options), ct);
        }
    }

    private static async Task WriteNewAsync(string path, string value, CancellationToken ct)
        => await LibraryPathGuard.WriteNewAsync(path, Encoding.UTF8.GetBytes(value), ct);

    private static async Task WriteAtomicAsync(string path, string value, CancellationToken ct)
        => await LibraryPathGuard.ReplaceAsync(path, Encoding.UTF8.GetBytes(value), ct);

    private static string Illustration(BaseTemplateInfo item)
    {
        var motif = item.Id switch
        {
            "portfolio-grid" => "<rect x='160' y='160' width='360' height='360' rx='180'/><rect x='610' y='120' width='370' height='480' rx='12' opacity='.4'/><path d='M80 640L580 180 1110 630' fill='none' stroke='currentColor' stroke-width='8'/>",
            "field-journal" => "<path d='M600 630V140M600 490Q350 420 270 210Q530 220 600 400M600 370Q790 100 1010 160Q880 420 600 480' fill='none' stroke='currentColor' stroke-width='17'/><circle cx='240' cy='560' r='90' opacity='.2'/>",
            "architecture-studio" => "<path d='M200 600V280L610 100 1020 280V600Z' fill='none' stroke='currentColor' stroke-width='5'/><path d='M610 100V600M200 280L610 460 1020 280M610 460V600M330 600V340M470 600V400M750 600V400M890 600V340' fill='none' stroke='currentColor' stroke-width='3'/><path d='M150 620H1070' stroke='currentColor' stroke-width='6'/>",
            "course-notebook" => "<rect x='230' y='150' width='700' height='460' rx='16' opacity='.12'/><path d='M300 240H810M300 320H730M300 400H790M300 480H550' stroke='currentColor' stroke-width='12'/><circle cx='850' cy='500' r='95'/><path d='M805 500L840 535 900 465' fill='none' stroke='white' stroke-width='12'/>",
            "recipe-kitchen" => "<circle cx='600' cy='380' r='260' opacity='.12'/><circle cx='600' cy='380' r='200' fill='none' stroke='currentColor' stroke-width='6'/><path d='M430 460Q470 180 700 240Q890 330 680 520Z' opacity='.6'/><path d='M270 160V600M220 160V310H320V160M970 160V600' fill='none' stroke='currentColor' stroke-width='12'/>",
            "event-poster" => "<circle cx='600' cy='380' r='280' fill='none' stroke='currentColor' stroke-width='50'/><path d='M120 590L550 120 1090 570' fill='none' stroke='currentColor' stroke-width='45'/><circle cx='880' cy='170' r='75' opacity='.5'/>",
            "travel-atlas" => "<path d='M90 530Q210 230 400 470T730 280T1120 230' fill='none' stroke='currentColor' stroke-width='12' stroke-dasharray='18 18'/><path d='M150 580L330 190 490 540 720 110 1080 580Z' opacity='.18'/><circle cx='310' cy='395' r='30'/><circle cx='760' cy='240' r='30'/><circle cx='1090' cy='245' r='30'/>",
            "product-launch" => "<rect x='320' y='160' width='570' height='420' rx='50' opacity='.25'/><rect x='380' y='210' width='450' height='310' rx='20' fill='none' stroke='currentColor' stroke-width='10'/><circle cx='600' cy='360' r='100'/><path d='M565 315L650 365 565 410Z' fill='white'/>",
            "podcast-studio" => "<rect x='495' y='135' width='210' height='320' rx='105'/><path d='M430 330V375A170 170 0 0 0 770 375V330M600 550V630M490 630H710' fill='none' stroke='currentColor' stroke-width='17'/><path d='M140 270V490M220 330V430M990 270V490M1070 320V440' stroke='currentColor' stroke-width='20'/>",
            "gallery-wall" => "<rect x='120' y='160' width='290' height='430' opacity='.2'/><circle cx='260' cy='330' r='100'/><rect x='470' y='90' width='230' height='520' fill='none' stroke='currentColor' stroke-width='10'/><path d='M460 570L730 170 1000 570Z' opacity='.6'/><circle cx='990' cy='180' r='95' opacity='.3'/>",
            "documentation-desk" => "<rect x='170' y='140' width='860' height='490' rx='15' fill='none' stroke='currentColor' stroke-width='8'/><path d='M170 240H1030M370 240V630M430 320H890M430 410H770M430 500H850' stroke='currentColor' stroke-width='10'/><circle cx='230' cy='190' r='13'/><circle cx='280' cy='190' r='13'/>",
            "nonprofit-story" => "<circle cx='410' cy='270' r='100'/><circle cx='790' cy='270' r='100' opacity='.5'/><path d='M170 640Q220 340 410 390Q600 340 650 640M550 640Q600 340 790 390Q980 340 1030 640' opacity='.25'/><path d='M490 440L600 560 710 440' fill='none' stroke='currentColor' stroke-width='22'/>",
            "wellness-retreat" => "<circle cx='600' cy='245' r='140' opacity='.5'/><path d='M70 610Q260 260 480 480T1120 390V720H70Z' opacity='.22'/><path d='M50 630Q320 440 620 530T1180 560' fill='none' stroke='currentColor' stroke-width='8'/><path d='M860 540Q780 200 960 160Q1070 360 860 540' opacity='.55'/>",
            "maker-workbench" => "<path d='M180 570L600 150 1020 570Z' fill='none' stroke='currentColor' stroke-width='14'/><circle cx='590' cy='350' r='110' opacity='.2'/><path d='M210 200L910 620M190 220L890 640' stroke='currentColor' stroke-width='15'/><rect x='700' y='160' width='160' height='160' rx='16' transform='rotate(20 780 240)' opacity='.5'/>",
            _ => "<path d='M160 600V160M160 600H1090' fill='none' stroke='currentColor' stroke-width='7'/><rect x='250' y='390' width='125' height='210' opacity='.35'/><rect x='470' y='260' width='125' height='340' opacity='.55'/><rect x='690' y='330' width='125' height='270' opacity='.7'/><rect x='910' y='160' width='125' height='440'/><path d='M200 430L400 270 620 310 840 160 1050 100' fill='none' stroke='currentColor' stroke-width='8'/>"
        };
        return $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 1200 760' role='img' aria-labelledby='title'><title id='title'>{System.Net.WebUtility.HtmlEncode(item.Title)} — original geometric illustration</title><rect width='1200' height='760' fill='{item.Background}'/><g color='{item.Accent}' fill='currentColor'>{motif}</g><text x='60' y='710' font-family='system-ui,sans-serif' font-size='18' letter-spacing='5' fill='{item.Ink}'>{System.Net.WebUtility.HtmlEncode(item.Category.ToUpperInvariant())}</text></svg>";
    }
}
