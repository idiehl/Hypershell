using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HyperLib.Core;
using Hypershell.Models;

namespace Hypershell.Services;

public sealed class StudioHyperlibWorkPublicationReview
{
    public string SourceWorkRoot { get; init; } = "";
    public string SourceLibraryRoot { get; init; } = "";
    public string DestinationLibraryRoot { get; init; } = "";
    public string Title { get; init; } = "";
    public string WorkId { get; init; } = "";
    public string Fingerprint { get; init; } = "";
    public long SourceRevision { get; init; }
    public long ExpectedDestinationRevision { get; init; }
    public int PageCount { get; init; }
    public int NodeCount { get; init; }
    public int SourceFileCount { get; init; }
    public int MediaFileCount { get; init; }
    public long Bytes { get; init; }
    public string ConflictMessage { get; init; } = "";
    public bool CanPublish => ConflictMessage.Length == 0;
    internal StudioWorkImportSnapshot Snapshot { get; init; } = new();
}

public sealed record StudioHyperlibWorkPublicationResult(string WorkId, string DestinationLibraryRoot, string DestinationWorkRoot, long Revision);

/// <summary>Publishes a saved Studio Work as an independent copy in the local Hyperlib reader library.</summary>
public sealed class StudioHyperlibWorkPublicationService
{
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", ".git", ".hyperlib", ".vs", "node_modules", "artifacts", "output" };

    public string? GetDefaultDestination()
    {
        // Read the registered reader definition directly: opening a review never rewrites registry or command trust.
        var shared = Environment.GetEnvironmentVariable(SettingsService.SharedMyNetDirectoryOverrideVariable);
        var directory = !string.IsNullOrWhiteSpace(shared) ? shared : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hypershell", "AtlasMyNet");
        var path = Path.Combine(directory, "apps.json");
        if (!File.Exists(path)) return null;
        try
        {
            GuardFile(path);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
            var document = JsonSerializer.Deserialize<AtlasMyNetAppDocument>(File.ReadAllText(path), options);
            var definition = document?.Apps.FirstOrDefault(app => app.Id == HyperlibMyNetRegistration.AppId);
            return definition is null ? null : HyperlibMyNetRegistration.GetTrustedRoot(definition);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return null; }
    }

    public async Task<StudioHyperlibWorkPublicationReview> ReviewAsync(string workProjectRoot, string destinationLibraryRoot, CancellationToken ct = default)
    {
        var project = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workProjectRoot)); GuardFile(project);
        var sourceRoot = Path.GetFullPath(Path.Combine(project, "../..")); GuardFile(sourceRoot);
        var markerPath = Path.Combine(sourceRoot, ".studio", "hyperlib-work.json"); GuardFile(markerPath);
        if (!HyperlibWorkPreviewHostService.IsGeneratedLibrary(sourceRoot)) throw new InvalidDataException("Publish a generated, independent Studio Hyperlib Work project.");
        using var marker = JsonDocument.Parse(await File.ReadAllTextAsync(markerPath, ct));
        var workId = marker.RootElement.GetProperty("workId").GetString() ?? throw new InvalidDataException("The Studio project marker has no Work identity.");
        if (!SamePath(project, Path.Combine(sourceRoot, "Works", workId))) throw new InvalidDataException("Choose the Work project identified by its Studio project marker.");
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationLibraryRoot)); GuardFile(destination);
        if (SamePath(destination, sourceRoot) || destination.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || sourceRoot.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Choose a separate Hyperlib reader library as the publication destination.");
        if (!File.Exists(Path.Combine(destination, "hyperlib.app.json")) || !File.Exists(Path.Combine(destination, "Content", "library.json")))
            throw new InvalidDataException("Choose an existing Hyperlib library root containing hyperlib.app.json and Content/library.json.");
        var source = await ReadStateAsync(sourceRoot, ct); var target = await ReadStateAsync(destination, ct);
        var work = source.Nodes.SingleOrDefault(node => node.Id == workId && node.Type == "work") ?? throw new InvalidDataException("The saved project content has no matching Work.");
        var nodes = source.Nodes.Where(node => node.Id == workId || node.WorkId == workId).Select(ContentJson.Clone).ToList();
        var pages = source.Pages.Where(pair => pair.Value.WorkId == workId).ToDictionary(pair => pair.Key, pair => ContentJson.Clone(pair.Value), StringComparer.Ordinal);
        var templatePath = Guard(project, "authoring.template.json");
        var template = JsonSerializer.Deserialize<TemplateDefinition>(await File.ReadAllTextAsync(templatePath, ct), ContentJson.Options) ?? throw new InvalidDataException("The Work's Page template is empty.");
        TemplateValidator.ValidateDefinition(template);
        foreach (var page in pages.Values) TemplateValidator.ValidatePage(template, page);
        var files = new List<StudioWorkImportFile>(); long bytes = 0; var fileCount = 0;
        async Task AddFileAsync(string path, string relative)
        {
            GuardFile(path);
            var info = new FileInfo(path);
            if (++fileCount > 10000 || (bytes += info.Length) > 512L * 1024 * 1024) throw new InvalidDataException("The Work publication exceeds 10,000 files or 512 MiB.");
            var content = await File.ReadAllBytesAsync(path, ct);
            files.Add(new() { RelativePath = relative.Replace('\\', '/'), Bytes = content, Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)) });
        }
        foreach (var path in EnumerateProject(project))
            await AddFileAsync(path, "Works/" + workId + "/" + Path.GetRelativePath(project, path));
        var ownedRuntime = Path.Combine(project, "Templates", "StudioRuntime", "HyperLib.Runtime", "HyperLib.Runtime.csproj");
        if (File.Exists(Path.Combine(project, HyperlibProjectPipelineService.PipelineRelativePath)) && !File.Exists(ownedRuntime))
            throw new InvalidDataException("This Work needs its project-owned rendering SDK before publication. Create a staged project with current Studio, or make this Work's SDK and project references portable before publishing. Your existing project is preserved.");
        foreach (var file in files.Where(file => Path.GetExtension(file.RelativePath) == ".csproj"))
        {
            var relative = file.RelativePath[("Works/" + workId + "/").Length..];
            var csprojRoot = Path.GetDirectoryName(Path.Combine(project, relative))!;
            var xml = XDocument.Parse(Encoding.UTF8.GetString(file.Bytes));
            foreach (var reference in xml.Descendants().Where(element => element.Name.LocalName == "ProjectReference"))
            {
                var include = (string?)reference.Attribute("Include") ?? "";
                if (include.Length == 0 || Path.IsPathRooted(include) || include.Contains("$(", StringComparison.Ordinal))
                    throw new InvalidDataException("Publication requires portable project references inside this Work: " + relative);
                var referenced = Path.GetFullPath(Path.Combine(csprojRoot, include)); GuardFile(referenced);
                var workRelative = Path.GetRelativePath(project, referenced).Replace('\\', '/');
                if (!referenced.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || !files.Any(item => item.RelativePath == "Works/" + workId + "/" + workRelative))
                    throw new InvalidDataException("A source project depends on files outside this Work: " + relative + ". Create a staged project with current Studio, or make this Work's SDK and project references portable before publishing. Your existing project is preserved.");
            }
        }
        var sourceFiles = files.Count;
        var references = JsonSerializer.Serialize(nodes, ContentJson.Options) + "\n" + JsonSerializer.Serialize(pages, ContentJson.Options) + "\n" + JsonSerializer.Serialize(template, ContentJson.Options)
            // Shared SDK code contains generic /media/ regular expressions, not literal Work assets.
            // Work component source and styles remain scanned for their concrete local media dependencies.
            + "\n" + string.Join('\n', files.Where(file => !file.RelativePath.Contains("/Templates/StudioRuntime/", StringComparison.Ordinal)
                && Path.GetExtension(file.RelativePath) is ".razor" or ".css" or ".js" or ".cs" or ".html").Select(file => Encoding.UTF8.GetString(file.Bytes)));
        var media = Regex.Matches(references, "/media/([^\\s\"'<>?()\\\\]+)").Select(match => Uri.UnescapeDataString(match.Groups[1].Value).TrimEnd(';', ',', '}'))
            .Distinct(StringComparer.Ordinal).ToArray();
        foreach (var relative in media)
        {
            var path = Guard(sourceRoot, "Media/" + relative);
            if (!File.Exists(path)) throw new FileNotFoundException("A referenced media file is missing from the saved Work: " + relative, path);
            await AddFileAsync(path, "Media/" + relative);
            var metadata = path + ".metadata.json";
            if (File.Exists(metadata)) await AddFileAsync(metadata, "Media/" + relative + ".metadata.json");
        }
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        digest.AppendData(JsonSerializer.SerializeToUtf8Bytes(new { source.Revision, nodes, pages }, ContentJson.Options));
        foreach (var file in files.OrderBy(file => file.RelativePath, StringComparer.Ordinal)) digest.AppendData(Encoding.UTF8.GetBytes(file.RelativePath + ":" + file.Sha256 + "\n"));
        var fingerprint = Convert.ToHexStringLower(digest.GetHashAndReset());
        var conflict = target.Nodes.Any(node => nodes.Any(imported => imported.Id.Equals(node.Id, StringComparison.OrdinalIgnoreCase)))
            || Directory.Exists(Path.Combine(destination, "Works", workId)) || File.Exists(Path.Combine(destination, "Works", workId))
            ? "This Work has already been published, or its stable IDs/source directory are already present. The existing publication will not be overwritten. Choose another library or create a separately identified Work." : "";
        return new()
        {
            SourceWorkRoot = project, SourceLibraryRoot = sourceRoot, DestinationLibraryRoot = destination, Title = work.Title, WorkId = workId,
            SourceRevision = source.Revision, ExpectedDestinationRevision = target.Revision, Fingerprint = fingerprint, PageCount = pages.Count,
            NodeCount = nodes.Count, SourceFileCount = sourceFiles, MediaFileCount = media.Length, Bytes = bytes, ConflictMessage = conflict,
            Snapshot = new() { WorkId = workId, Title = work.Title, Fingerprint = fingerprint, Nodes = nodes, Pages = pages, Template = template, Files = files }
        };
    }

    public async Task<StudioHyperlibWorkPublicationResult> PublishAsync(StudioHyperlibWorkPublicationReview review, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (!review.CanPublish) throw new InvalidOperationException(review.ConflictMessage);
        var current = await ReviewAsync(review.SourceWorkRoot, review.DestinationLibraryRoot, ct);
        if (!current.CanPublish) throw new InvalidOperationException(current.ConflictMessage);
        if (current.Fingerprint != review.Fingerprint) throw new InvalidOperationException("The Work changed after the publication review. Save your content and review it again before publishing.");
        var store = new ContentStore(review.DestinationLibraryRoot);
        var published = await store.ImportWorkSnapshotAsync(review.Snapshot, review.ExpectedDestinationRevision, ct);
        return new(review.WorkId, review.DestinationLibraryRoot, Path.Combine(review.DestinationLibraryRoot, "Works", review.WorkId), published.Revision);
    }
    private static async Task<LibraryState> ReadStateAsync(string root, CancellationToken ct)
    {
        var path = Guard(root, "Content/library.json");
        if (new FileInfo(path).Length > 32L * 1024 * 1024) throw new InvalidDataException("The saved library content exceeds its 32 MiB JSON limit.");
        // The journal is authoritative. A normal store read verifies its checksum chain and repairs a stale projection.
        // Reading library.json alone could publish old content after an interrupted projection write.
        return await new ContentStore(root).ReadAsync(ct);
    }
    private static IEnumerable<string> EnumerateProject(string root, bool projectRoot = true)
    {
        GuardFile(root);
        foreach (var path in Directory.EnumerateFiles(root).Order(StringComparer.Ordinal))
        {
            GuardFile(path);
            var name = Path.GetFileName(path);
            if (name is "publication.json" or "project.lock" or "authoring-draft.json" || Path.GetExtension(path) is ".tmp" or ".bak"
                || Path.GetFileName(root) == ".studio" && (name.StartsWith("recovery", StringComparison.OrdinalIgnoreCase) || name.Contains(".recovery.", StringComparison.OrdinalIgnoreCase))) continue;
            yield return path;
        }
        foreach (var directory in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
            if (!Ignored.Contains(Path.GetFileName(directory)) && !(projectRoot && Path.GetFileName(directory) == "Content")
                && !(Path.GetFileName(root) == ".studio" && Path.GetFileName(directory) is "recovery" or "drafts")) foreach (var path in EnumerateProject(directory, false)) yield return path;
    }
    private static string Guard(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split('/', '\\').Any(part => part is "." or ".." or "")) throw new InvalidDataException("Publication paths must stay inside their owning project.");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Publication path escapes its owning project.");
        GuardFile(path); return path;
    }
    private static void GuardFile(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Publication paths cannot traverse symbolic links or junctions.");
    }
    private static bool SamePath(string left, string right) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
}
