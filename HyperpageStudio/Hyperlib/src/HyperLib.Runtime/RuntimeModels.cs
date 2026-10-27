using System.Text.Json;

namespace HyperLib.Runtime;

public sealed record RuntimeGeneration(string PackageId, string GenerationId, string ProxyPrefix,
    string UpstreamUrl, string RuntimePath, int ProcessId, DateTimeOffset ActivatedAtUtc);

public sealed record RuntimeStatus(string PackageId, string State, string? Error, RuntimeGeneration? Active);

public sealed class BlockContext
{
    public string Id { get; init; } = "";
    public bool Editing { get; init; }
    public Dictionary<string, JsonElement> Values { get; init; } = new();
    public string Text(string name, string fallback = "") => Values.TryGetValue(name, out var value)
        ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : value.ToString() : fallback;
    public double Number(string name, double fallback = 0) => Values.TryGetValue(name, out var value)
        && value.TryGetDouble(out var number) ? number : fallback;
}
