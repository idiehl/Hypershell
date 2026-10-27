using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Meridian.Host;

/// <summary>Bounded grid metadata and immediate-child addressing; keep in step with core/grid.ts.</summary>
internal static partial class GridSystems
{
    private const int MaxAxis = 512;
    private const int MaxCells = 16384;
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
    [GeneratedRegex("^[A-Za-z]+[1-9][0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex AddressPattern();
    [GeneratedRegex("^([1-9][0-9]*):([1-9][0-9]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex CoordinatePattern();

    private static string? Text(JsonObject node, string key) => node[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    public static void ValidateNode(JsonObject node)
    {
        if (node.ContainsKey("childArrangement") && Text(node, "childArrangement") is not ("free" or "grid" or "row" or "column" or "radial" or "stack"))
            throw new ApiException(400, "Child arrangement must be free, grid, row, column, radial or stack.");
        if (node.TryGetPropertyValue("grid", out var gridValue))
        {
            if (gridValue is not JsonObject grid) throw new ApiException(400, "Grid settings must be an object.");
            Fields(grid, ["columns", "rows", "cellWidth", "cellHeight", "gap", "showHeaders", "fillOrder", "windowColumns", "windowRows", "scrollDirection"], "grid");
            if (Axis(grid, "columns") * Axis(grid, "rows") > MaxCells) throw new ApiException(400, "A grid can contain at most 16384 cells.");
            Number(grid, "cellWidth", 36, 10000); Number(grid, "cellHeight", 36, 10000);
            if (grid.ContainsKey("gap")) Number(grid, "gap", 0, 1000);
            if (grid.ContainsKey("showHeaders") && (grid["showHeaders"] is not JsonValue flag || !flag.TryGetValue<bool>(out _)))
                throw new ApiException(400, "Grid showHeaders must be Boolean.");
            if (grid.ContainsKey("fillOrder") && Text(grid, "fillOrder") is not ("row" or "column"))
                throw new ApiException(400, "Grid fillOrder must be row or column.");
            if (grid.ContainsKey("scrollDirection") && Text(grid, "scrollDirection") is not ("vertical" or "horizontal"))
                throw new ApiException(400, "Window scrollDirection must be vertical or horizontal.");
            if (grid.ContainsKey("windowColumns")) Axis(grid, "windowColumns");
            if (grid.ContainsKey("windowRows")) Axis(grid, "windowRows");
        }
        if (node.TryGetPropertyValue("cell", out var cellValue))
        {
            if (cellValue is not JsonObject cell) throw new ApiException(400, "Cell metadata must be an object.");
            if (Text(node, "parentId") is null) throw new ApiException(400, "A cell must be an immediate child of a parent node.");
            Fields(cell, ["column", "row", "name"], "cell");
            Axis(cell, "column"); Axis(cell, "row");
            if (cell.ContainsKey("name") && (Text(cell, "name") is not { } name || !ValidName(name)))
                throw new ApiException(400, "Cell names must be trimmed identifiers of 1 to 64 characters and cannot be A1 addresses.");
        }
        if (node.ContainsKey("targetCell"))
        {
            var target = Text(node, "targetCell");
            if (Text(node, "kind") != "reference" || string.IsNullOrEmpty(Text(node, "targetId")) || target is null || !ValidTarget(target))
                throw new ApiException(400, "A target cell requires a reference target and a trimmed X:Y coordinate, legacy A1 address or cell name of 1 to 64 characters.");
        }
    }

    public static void ValidateCells(Dictionary<string, JsonObject> nodes)
    {
        var coordinates = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var names = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var node in nodes.Values)
        {
            if (node["cell"] is not JsonObject cell) continue;
            var parentId = Text(node, "parentId")!;
            var column = Axis(cell, "column"); var row = Axis(cell, "row");
            if (!coordinates.TryGetValue(parentId, out var occupied)) coordinates[parentId] = occupied = new(StringComparer.Ordinal);
            if (!occupied.Add(column + ":" + row)) throw new ApiException(400, "Immediate children cannot occupy the same grid cell.");
            if (Text(cell, "name") is { } name)
            {
                if (!names.TryGetValue(parentId, out var used)) names[parentId] = used = new(StringComparer.OrdinalIgnoreCase);
                if (!used.Add(name)) throw new ApiException(400, "Cell names must be unique within their parent, ignoring case.");
            }
            if (nodes[parentId]["grid"] is JsonObject grid && (column > Axis(grid, "columns") || row > Axis(grid, "rows")))
                throw new ApiException(400, "A child cell must lie within its parent's grid dimensions.");
        }
    }

    public static JsonObject? ResolveLiveReference(JsonObject reference, Dictionary<string, JsonObject> nodes)
    {
        if (Text(reference, "targetId") is not { } targetId || !nodes.TryGetValue(targetId, out var parent)
            || Text(parent, "canvasId") != Text(reference, "canvasId")) return null;
        return Text(reference, "targetCell") is { } target ? FindCell(nodes.Values, parent, target) : parent;
    }

    public static JsonObject? ResolveSavedReference(JsonObject reference, JsonObject snapshots)
    {
        if (Text(reference, "targetId") is not { } targetId || snapshots[targetId] is not JsonObject parentSnapshot
            || parentSnapshot["node"] is not JsonObject parent || Text(parent, "id") != targetId
            || Text(parent, "canvasId") == Text(reference, "canvasId")) return null;
        if (Text(reference, "targetCell") is not { } target) return parentSnapshot;
        var savedNodes = snapshots.Select(pair => pair.Value is JsonObject snapshot ? snapshot["node"] as JsonObject : null).OfType<JsonObject>();
        var cell = FindCell(savedNodes, parent, target);
        return cell is not null && Text(cell, "id") is { } id ? snapshots[id] as JsonObject : null;
    }

    private static JsonObject? FindCell(IEnumerable<JsonObject> nodes, JsonObject parent, string target)
    {
        var address = Address(target);
        return nodes.FirstOrDefault(node => Text(node, "parentId") == Text(parent, "id") && Text(node, "canvasId") == Text(parent, "canvasId")
            && node["cell"] is JsonObject cell && (address is { } position
                ? Axis(cell, "column") == position.Column && Axis(cell, "row") == position.Row
                : string.Equals(Text(cell, "name"), target, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool ValidName(string name) => name == name.Trim() && NamePattern().IsMatch(name) && !AddressPattern().IsMatch(name);
    private static bool ValidTarget(string target) => target.Length is >= 1 and <= 64 && target == target.Trim()
        && (AddressPattern().IsMatch(target) || CoordinatePattern().IsMatch(target) ? Address(target) is not null : ValidName(target));
    private static (int Column, int Row)? Address(string value)
    {
        var coordinate = CoordinatePattern().Match(value);
        if (coordinate.Success)
            return int.TryParse(coordinate.Groups[1].Value, out var x) && int.TryParse(coordinate.Groups[2].Value, out var y)
                && x is >= 1 and <= MaxAxis && y is >= 1 and <= MaxAxis ? (x, y) : null;
        if (!AddressPattern().IsMatch(value)) return null;
        var column = 0; var at = 0;
        while (at < value.Length && char.IsAsciiLetter(value[at]))
        {
            column = column * 26 + char.ToUpperInvariant(value[at++]) - 'A' + 1;
            if (column > MaxAxis) return null;
        }
        return int.TryParse(value.AsSpan(at), out var row) && row is >= 1 and <= MaxAxis ? (column, row) : null;
    }

    private static int Axis(JsonObject settings, string key)
    {
        if (settings[key] is not JsonValue value || !value.TryGetValue<double>(out var number) || !double.IsFinite(number)
            || number != Math.Truncate(number) || number is < 1 or > MaxAxis)
            throw new ApiException(400, $"Grid '{key}' must be an integer from 1 to 512.");
        return (int)number;
    }
    private static void Number(JsonObject settings, string key, double minimum, double maximum)
    {
        if (settings[key] is not JsonValue value || !value.TryGetValue<double>(out var number) || !double.IsFinite(number) || number < minimum || number > maximum)
            throw new ApiException(400, $"Grid '{key}' must be a finite number from {minimum} to {maximum}.");
    }
    private static void Fields(JsonObject settings, string[] allowed, string kind)
    {
        if (settings.Any(pair => !allowed.Contains(pair.Key))) throw new ApiException(400, $"Unknown {kind} setting.");
    }
}
