using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Meridian.Host;

public static class ProjectPackage
{
    public static byte[] Export(JsonObject project)
    {
        ProjectValidator.Validate(project);
        var manifest = (JsonObject)project.DeepClone();
        using var output = new MemoryStream();
        long expandedBytes = 0;
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var written = new HashSet<string>(StringComparer.Ordinal);
            foreach (var asset in ProjectValidator.Assets(manifest))
            {
                var bytes = ProjectValidator.AssetBytes(asset);
                var name = "assets/" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + ".bin";
                if (written.Add(name))
                {
                    if (written.Count > 1023) throw new ApiException(413, "The project contains too many distinct attachments.");
                    expandedBytes += bytes.Length;
                    using var stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
                    stream.Write(bytes);
                }
                asset.Remove("base64");
                asset["entry"] = name;
            }
            using var manifestStream = archive.CreateEntry("project.json", CompressionLevel.Optimal).Open();
            var json = Encoding.UTF8.GetBytes(manifest.ToJsonString());
            if (expandedBytes + json.LongLength > ProjectValidator.MaxExpandedBytes) throw new ApiException(413, "The expanded project package exceeds 80 MiB.");
            manifestStream.Write(json);
        }
        if (output.Length > ProjectValidator.MaxRequestBytes) throw new ApiException(413, "The project package is too large.");
        return output.ToArray();
    }

    public static JsonObject Import(Stream input)
    {
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > 1024) throw new ApiException(413, "The project package contains too many entries.");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (!SafeEntry(name) || !entries.TryAdd(name, entry))
                throw new ApiException(400, "The project package contains an unsafe or duplicate entry.");
            if (entry.Length > ProjectValidator.MaxExpandedBytes || total > ProjectValidator.MaxExpandedBytes - entry.Length)
                throw new ApiException(413, "The expanded project package exceeds 80 MiB.");
            total += entry.Length;
        }
        if (!entries.TryGetValue("project.json", out var manifest)) throw new ApiException(400, "The package is missing project.json.");
        var project = JsonNode.Parse(ReadEntry(manifest, ProjectValidator.MaxExpandedBytes)) as JsonObject
            ?? throw new ApiException(400, "The project manifest must contain an object.");
        if (project["assets"] is not JsonArray || project["snapshots"] is not JsonObject)
            throw new ApiException(400, "The project manifest is missing asset or snapshot metadata.");
        var used = new HashSet<string>(StringComparer.Ordinal) { "project.json" };
        long reconstructedBytes = 0;
        foreach (var asset in ProjectValidator.Assets(project))
        {
            var name = ProjectValidator.Text(asset, "entry", 100);
            if (!name.StartsWith("assets/", StringComparison.Ordinal) || !entries.TryGetValue(name, out var entry))
                throw new ApiException(400, "A packaged attachment is missing.");
            var bytes = ReadEntry(entry, ProjectValidator.MaxAssetBytes);
            reconstructedBytes += bytes.Length;
            if (reconstructedBytes > ProjectValidator.MaxTotalAssetBytes) throw new ApiException(413, "Project attachments exceed 64 MiB.");
            var expected = "assets/" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + ".bin";
            if (!string.Equals(name, expected, StringComparison.Ordinal)) throw new ApiException(400, "A packaged attachment failed its content hash check.");
            asset.Remove("entry");
            asset["base64"] = Convert.ToBase64String(bytes);
            used.Add(name);
        }
        if (entries.Keys.Any(name => !used.Contains(name))) throw new ApiException(400, "The package contains unreferenced entries.");
        ProjectValidator.Validate(project);
        return project;
    }

    private static bool SafeEntry(string name) => name == "project.json"
        || name.StartsWith("assets/", StringComparison.Ordinal)
            && name.Length == "assets/".Length + 64 + ".bin".Length
            && name.EndsWith(".bin", StringComparison.Ordinal)
            && name.AsSpan(7, 64).IndexOfAnyExcept("0123456789abcdef") < 0;

    private static byte[] ReadEntry(ZipArchiveEntry entry, long limit)
    {
        if (entry.Length > limit) throw new ApiException(413, "A package entry is too large.");
        using var stream = entry.Open();
        using var output = new MemoryStream();
        var buffer = new byte[65536];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            if (output.Length > limit - read) throw new ApiException(413, "A package entry expands beyond its limit.");
            output.Write(buffer, 0, read);
        }
        if (output.Length != entry.Length) throw new ApiException(400, "A package entry is truncated.");
        return output.ToArray();
    }
}
