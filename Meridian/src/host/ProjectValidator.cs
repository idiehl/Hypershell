using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Meridian.Host;

public sealed class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public static partial class ProjectValidator
{
    public const long MaxRequestBytes = 100L * 1024 * 1024;
    public const long MaxExpandedBytes = 80L * 1024 * 1024;
    public const int MaxAssetBytes = 16 * 1024 * 1024;
    public const long MaxTotalAssetBytes = 64L * 1024 * 1024;
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,95}$")]
    private static partial Regex IdPattern();
    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorPattern();

    public static string SafeId(string id)
    {
        if (!IdPattern().IsMatch(id)) throw new ApiException(400, "Invalid project or checkpoint identifier.");
        return id;
    }

    public static string Text(JsonObject value, string name, int maximum = 256)
    {
        if (value[name] is not JsonValue node || !node.TryGetValue<string>(out var text)
            || string.IsNullOrWhiteSpace(text) || text.Length > maximum)
            throw new ApiException(400, $"'{name}' must be nonempty text with at most {maximum} characters.");
        return text;
    }

    public static int Integer(JsonObject value, string name)
    {
        if (value[name] is not JsonValue node || !node.TryGetValue<int>(out var number) || number < 0)
            throw new ApiException(400, $"'{name}' must be a nonnegative integer.");
        return number;
    }

    public static void Validate(JsonObject project, int maximumNodes = 50000)
    {
        if (Integer(project, "schemaVersion") != 1) throw new ApiException(400, "Unsupported Meridian project schema version.");
        SafeId(Text(project, "id", 96));
        Text(project, "title");
        Integer(project, "revision");
        var entry = Text(project, "entryCanvasId", 96);
        var canvases = Array(project, "canvases", 1000);
        var nodes = Array(project, "nodes", maximumNodes);
        var links = Array(project, "links", 100000);
        Array(project, "assets", 1000);
        Array(project, "dependencies", 1000);
        if (project["snapshots"] is not JsonObject snapshots || snapshots.Count > 50000)
            throw new ApiException(400, "A snapshots object with at most 50000 entries is required.");
        var canvasIds = UniqueIds(canvases, "Work");
        var nodeIds = UniqueIds(nodes, "node");
        UniqueIds(links, "link");
        UniqueIds((JsonArray)project["assets"]!, "asset");
        if (!canvasIds.Contains(entry)) throw new ApiException(400, "The entry Work does not exist.");
        foreach (var item in canvases)
        {
            var canvas = (JsonObject)item!;
            Text(canvas, "title");
            ValidateSnapping(canvas);
            ValidateAppearance(canvas, "canvas");
            if (!nodeIds.Contains(Text(canvas, "rootId", 96))) throw new ApiException(400, "A Work root does not exist.");
        }
        foreach (var item in nodes)
        {
            var node = (JsonObject)item!;
            ValidateSnapping(node);
            ValidateAppearance(node, "node");
            ValidateTextRuns(node);
            ValidatePositionLock(node);
            ValidateContainerSizes(node);
            ValidateCluster(node);
            ValidateSmartLink(node);
            ValidateExpansionTray(node);
            ValidateRetiredBlockContent(node);
            ValidateDependencies(node);
            ValidateRemoteLinks(node);
            ValidateAttachmentsAndHyperlinks(node);
            ValidateNodeHub(node);
            ValidateCanvasTarget(node);
            ValidateContainerVariant(node);
            if (!canvasIds.Contains(Text(node, "canvasId", 96))) throw new ApiException(400, "A node Work does not exist.");
            var kind = Text(node, "kind", 32);
            NodeSystems.ValidateNode(node);
            foreach (var name in new[] { "x", "y", "width", "height" })
                if (node[name] is not JsonValue number || !number.TryGetValue<double>(out var value)
                    || !double.IsFinite(value) || Math.Abs(value) > 1e9 || name is "width" or "height" && value <= 0)
                    throw new ApiException(400, "Node geometry must contain finite coordinates and positive dimensions.");
        }
        foreach (var entrySnapshot in snapshots)
        {
            if (entrySnapshot.Value is not JsonObject snapshot || snapshot["node"] is not JsonObject snapshotNode)
                throw new ApiException(400, "A saved snapshot must contain a node object.");
            ValidateSnapping(snapshotNode);
            ValidateAppearance(snapshotNode, "node");
            ValidateTextRuns(snapshotNode);
            ValidatePositionLock(snapshotNode);
            ValidateContainerSizes(snapshotNode);
            ValidateCluster(snapshotNode);
            ValidateSmartLink(snapshotNode);
            ValidateExpansionTray(snapshotNode);
            ValidateRetiredBlockContent(snapshotNode);
            ValidateDependencies(snapshotNode);
            ValidateRemoteLinks(snapshotNode);
            ValidateAttachmentsAndHyperlinks(snapshotNode);
            ValidateNodeHub(snapshotNode);
            ValidateCanvasTarget(snapshotNode);
            ValidateContainerVariant(snapshotNode);
            NodeSystems.ValidateNode(snapshotNode);
            if (snapshot.TryGetPropertyValue("assets", out var savedAssets))
            {
                if (savedAssets is not JsonArray extraAssets || extraAssets.Count > 1000)
                    throw new ApiException(400, "Saved Hub assets must contain at most 1000 entries.");
                UniqueIds(extraAssets, "saved Hub asset");
            }
            if (snapshot.TryGetPropertyValue("backgroundAsset", out var pinnedBackground)
                && (pinnedBackground is not JsonObject backgroundAsset || snapshotNode["style"] is not JsonObject snapshotStyle
                    || Text(backgroundAsset, "id", 96) != Text(snapshotStyle, "backgroundAssetId", 96)))
                throw new ApiException(400, "Saved snapshot background asset does not match its node.");
            if (snapshot["value"] is not null && !NodeSystems.Scalar(snapshot["value"])) throw new ApiException(400, "Snapshot values must be Boolean, finite numbers or null.");
            if (snapshotNode["kind"] is JsonValue snapshotKind && snapshotKind.TryGetValue<string>(out var savedKind)
                && savedKind == "module" && snapshotNode.ContainsKey("operation"))
                throw new ApiException(400, "Module nodes group contents and do not perform operations.");
        }
        var objectIds = new HashSet<string>(StringComparer.Ordinal) { Text(project, "id", 96) };
        foreach (var collection in new[] { canvases, nodes, links, (JsonArray)project["assets"]! })
            foreach (var item in collection)
                if (!objectIds.Add(Text((JsonObject)item!, "id", 96))) throw new ApiException(400, "Every included object must have a unique identity.");
        var remoteCount = 0;
        foreach (var item in nodes)
            if (((JsonObject)item!)["remoteLinks"] is JsonArray remoteLinks)
                foreach (var remoteLink in remoteLinks)
                {
                    if (++remoteCount > 20000) throw new ApiException(400, "A document supports at most 20000 conceptual links.");
                    if (!objectIds.Add(Text((JsonObject)remoteLink!, "id", 96))) throw new ApiException(400, "Every included object must have a unique identity.");
                }
        var smartLinkIds = nodes.Cast<JsonObject>().Where(node => node.ContainsKey("smartLink")).Select(node => Text(node, "id", 96)).ToHashSet(StringComparer.Ordinal);
        foreach (var item in links)
        {
            var link = (JsonObject)item!;
            if (Text(link, "kind", 32) != "visual" && (smartLinkIds.Contains(Text(link, "sourceId", 96)) || smartLinkIds.Contains(Text(link, "targetId", 96))))
                throw new ApiException(400, "Smart Links arrange visual branches and do not carry functional or numeric signals.");
            ValidateAppearance(link, "link");
            if (link.TryGetPropertyValue("label", out var label)
                && (label is not JsonValue labelValue || !labelValue.TryGetValue<string>(out var labelText)
                    || labelText.Length > 160 || labelText.Any(character => char.IsControl(character) && character is not ('\t' or '\r' or '\n'))))
                throw new ApiException(400, "Connection labels must be text with at most 160 characters.");
            if (!canvasIds.Contains(Text(link, "canvasId", 96))
                || !nodeIds.Contains(Text(link, "sourceId", 96)) || !nodeIds.Contains(Text(link, "targetId", 96)))
                throw new ApiException(400, "A link references a missing node or Work.");
        }
        NodeSystems.ValidateLinks(project);
        long assetTotal = 0;
        foreach (var asset in Assets(project))
        {
            Text(asset, "id", 96);
            Text(asset, "name", 512);
            var mime = Text(asset, "mime", 128);
            if (!mime.Contains('/') || mime.Any(char.IsControl))
                throw new ApiException(400, "An attachment MIME type is invalid.");
            var bytes = AssetBytes(asset);
            assetTotal += bytes.Length;
            if (assetTotal > MaxTotalAssetBytes) throw new ApiException(413, "Project attachments exceed 64 MiB.");
        }
        var localAssets = ((JsonArray)project["assets"]!).Cast<JsonObject>().ToDictionary(asset => Text(asset, "id", 96), StringComparer.Ordinal);
        foreach (var item in canvases.Concat(nodes)) ValidateBackgroundImage((JsonObject)item!, localAssets);
        foreach (var item in snapshots)
            if (item.Value is JsonObject snapshot && snapshot["node"] is JsonObject savedNode)
                ValidateBackgroundImage(savedNode, localAssets, snapshot["backgroundAsset"] as JsonObject);
    }

    private static void ValidateCluster(JsonObject node)
    {
        if (!node.TryGetPropertyValue("cluster", out var value)) return;
        if (value is not JsonObject cluster || cluster.Count != 1 || !cluster.ContainsKey("buttonShape")
            || cluster["buttonShape"] is not JsonValue setting || !setting.TryGetValue<string>(out var shape)
            || shape is not ("square" or "circle"))
            throw new ApiException(400, "Disclosure cluster settings contain only a square or circle buttonShape.");
        if (node["kind"] is not JsonValue kind || !kind.TryGetValue<string>(out var kindText) || kindText != "container"
            || !OptionalClusterSetting(node, "containerVariant", "contained", "spatial")
            || !OptionalClusterSetting(node, "childLayout", "contained")
            || !OptionalClusterSetting(node, "childArrangement", "free") || node.ContainsKey("grid"))
            throw new ApiException(400, "Disclosure clusters require a Freeform container with contained children and no grid.");
    }

    private static void ValidateSmartLink(JsonObject node)
    {
        if (!node.TryGetPropertyValue("smartLink", out var value)) return;
        if (value is not JsonObject settings || settings.Any(pair => pair.Key is not ("direction" or "gap" or "resolvedDirection"))
            || settings["direction"] is not JsonValue directionValue || !directionValue.TryGetValue<string>(out var direction)
            || direction is not ("auto" or "right" or "down" or "left" or "up")
            || settings["gap"] is not JsonValue gapValue || !gapValue.TryGetValue<double>(out var gap) || !double.IsFinite(gap) || gap < 8 || gap > 10000
            || settings.TryGetPropertyValue("resolvedDirection", out var resolved) && (resolved is not JsonValue resolvedValue || !resolvedValue.TryGetValue<string>(out var orientation) || orientation is not ("right" or "down" or "left" or "up")))
            throw new ApiException(400, "Smart Link settings contain a direction, spacing gap from 8 to 10000, and optional stable automatic orientation.");
        if (Text(node, "kind", 32) != "junction" || !OptionalClusterSetting(node, "utilityVariant", "joint")
            || !OptionalClusterSetting(node, "childLayout", "spatial") || !OptionalClusterSetting(node, "childArrangement", "free")
            || node.ContainsKey("containerVariant") || node.ContainsKey("grid") || node.ContainsKey("valueType"))
            throw new ApiException(400, "A Smart Link is a visual Utility with ordinary spatial children, not a cell pattern or functional operation.");
    }

    private static bool OptionalClusterSetting(JsonObject node, string name, params string[] allowed)
        => !node.TryGetPropertyValue(name, out var setting)
            || setting is JsonValue value && value.TryGetValue<string>(out var text) && allowed.Contains(text);

    private static void ValidateExpansionTray(JsonObject node)
    {
        if (!node.TryGetPropertyValue("expansionTray", out var value)) return;
        if (value is not JsonObject tray || tray.Any(pair => pair.Key is not ("offset" or "size" or "layoutVersion"))
            || tray["offset"] is not JsonObject offset || offset.Any(pair => pair.Key is not ("x" or "y")))
            throw new ApiException(400, "Expansion tray state contains a source-relative x/y offset and optional outer dimensions.");
        foreach (var name in new[] { "x", "y" })
            if (offset[name] is not JsonValue number || !number.TryGetValue<double>(out var coordinate)
                || !double.IsFinite(coordinate) || Math.Abs(coordinate) > 1e9)
                throw new ApiException(400, "Expansion tray offsets must be finite coordinates no greater than 1000000000 in magnitude.");
        double layoutVersion = 0;
        if (tray.TryGetPropertyValue("layoutVersion", out var layoutValue))
        {
            if (layoutValue is not JsonValue version || !version.TryGetValue<double>(out layoutVersion) || layoutVersion is not (2 or 3))
                throw new ApiException(400, "Expansion tray layoutVersion must be 2 or 3 when supplied.");
        }
        if (!tray.TryGetPropertyValue("size", out var sizeValue)) return;
        if (sizeValue is not JsonObject size || size.Any(pair => pair.Key is not ("width" or "height")))
            throw new ApiException(400, "Expansion tray size contains only outer width and height.");
        foreach (var name in new[] { "width", "height" })
            if (size[name] is not JsonValue number || !number.TryGetValue<double>(out var dimension)
                || !double.IsFinite(dimension) || dimension <= (layoutVersion == 3 ? 10 : name == "width" || layoutVersion == 2 ? 40 : 72) || dimension > 1e9)
                throw new ApiException(400, "Expansion tray dimensions must exceed 10 for compact trays, 40 for version 2, and legacy height must exceed 72; both must be finite and no greater than 1000000000.");
    }

    private static void ValidateContainerSizes(JsonObject node)
    {
        if (!node.TryGetPropertyValue("containerSizes", out var value)) return;
        if (value is not JsonObject sizes || sizes.Any(pair => pair.Key is not ("expanded" or "collapsed")))
            throw new ApiException(400, "Container frame records contain only expanded and collapsed sizes.");
        foreach (var pair in sizes)
        {
            if (pair.Value is not JsonObject size || size.Any(setting => setting.Key is not ("width" or "height" or "sizeMode"))
                || size["sizeMode"] is not JsonValue mode || !mode.TryGetValue<string>(out var modeText) || modeText is not ("auto" or "fixed"))
                throw new ApiException(400, "Container frames require dimensions and automatic or fixed sizing.");
            foreach (var name in new[] { "width", "height" })
                if (size[name] is not JsonValue number || !number.TryGetValue<double>(out var dimension)
                    || !double.IsFinite(dimension) || dimension <= 0 || dimension > 1e9)
                    throw new ApiException(400, "Container frame dimensions must be positive finite numbers no greater than 1000000000.");
        }
    }

    private static void ValidateRetiredBlockContent(JsonObject node)
    {
        if (!node.TryGetPropertyValue("retiredBlockContent", out var value)) return;
        if (value is not JsonObject archive || archive.Any(pair => pair.Key is not ("title" or "text" or "titleRuns" or "textRuns" or "contentType"))
            || archive["title"] is not JsonValue title || !title.TryGetValue<string>(out _))
            throw new ApiException(400, "Retired Block content must contain an inert canonical title and optional text formatting.");
        if (archive.TryGetPropertyValue("text", out var text) && (text is not JsonValue textValue || !textValue.TryGetValue<string>(out _)))
            throw new ApiException(400, "Retired Block text must be canonical plain text.");
        if (archive.TryGetPropertyValue("contentType", out var contentType)
            && (contentType is not JsonValue type || !type.TryGetValue<string>(out var typeText) || typeText is not ("text" or "asset" or "link")))
            throw new ApiException(400, "Retired Block content selects one inert content type.");
        ValidateTextRuns(archive);
    }

    private static void ValidatePositionLock(JsonObject node)
    {
        if (node.TryGetPropertyValue("positionLocked", out var value)
            && (value is not JsonValue flag || !flag.TryGetValue<bool>(out _)))
            throw new ApiException(400, "Node position lock must be Boolean.");
    }

    private static void ValidateTextRuns(JsonObject node)
    {
        foreach (var (field, runField) in new[] { ("title", "titleRuns"), ("text", "textRuns") })
        {
            if (!node.TryGetPropertyValue(runField, out var value)) continue;
            var canonical = "";
            if (node.TryGetPropertyValue(field, out var canonicalValue)
                && (canonicalValue is not JsonValue textValue || !textValue.TryGetValue<string>(out canonical)))
                throw new ApiException(400, "Formatted passages must belong to canonical plain text.");
            if (value is not JsonArray runs || runs.Count > 4096) throw new ApiException(400, "Text supports at most 4096 formatted passages.");
            var previousEnd = 0;
            bool Boundary(int position) => position >= 0 && position <= canonical.Length
                && (position == 0 || position == canonical.Length || !char.IsHighSurrogate(canonical[position - 1]) || !char.IsLowSurrogate(canonical[position]));
            foreach (var item in runs)
            {
                if (item is not JsonObject run || run.Any(pair => pair.Key is not ("start" or "end" or "style"))
                    || run["start"] is not JsonValue startValue || !startValue.TryGetValue<int>(out var start)
                    || run["end"] is not JsonValue endValue || !endValue.TryGetValue<int>(out var end)
                    || start >= end || start < previousEnd || !Boundary(start) || !Boundary(end)
                    || run["style"] is not JsonObject style || style.Count == 0)
                    throw new ApiException(400, "Formatted passages require sorted nonoverlapping UTF-16 offsets without split surrogate pairs.");
                previousEnd = end;
                foreach (var pair in style)
                {
                    if (pair.Value is not JsonValue setting) throw new ApiException(400, "Passage formatting values must be inert typography settings.");
                    var valid = pair.Key switch
                    {
                        "fontFamily" => setting.TryGetValue<string>(out var family) && FontFamilies.Contains(family),
                        "fontSize" => StyleNumber(setting, 1, 512),
                        "color" => setting.TryGetValue<string>(out var color) && ColorPattern().IsMatch(color),
                        "bold" or "italic" or "underline" => setting.TryGetValue<bool>(out _),
                        _ => false
                    };
                    if (!valid) throw new ApiException(400, $"Invalid passage formatting setting '{pair.Key}'.");
                }
            }
        }
    }

    private static void ValidateContainerVariant(JsonObject node)
    {
        if (!node.TryGetPropertyValue("containerVariant", out var value)) return;
        if (node["kind"] is not JsonValue kind || !kind.TryGetValue<string>(out var kindText) || kindText == "canvas" || !NodeSystems.Kinds.Contains(kindText)
            || value is not JsonValue setting || !setting.TryGetValue<string>(out var variant) || variant is not ("spatial" or "contained" or "horizontal" or "vertical" or "grid" or "spreadsheet" or "gallery" or "window"))
            throw new ApiException(400, "Display patterns belong to non-root nodes and must use a supported pattern.");
        if (variant == "window" && (node["grid"] is not JsonObject grid || !grid.ContainsKey("windowColumns") || !grid.ContainsKey("windowRows")))
            throw new ApiException(400, "Window containers require full grid dimensions and visible windowColumns and windowRows.");
    }

    private static void ValidateRemoteLinks(JsonObject node)
    {
        if (!node.TryGetPropertyValue("remoteLinks", out var value)) return;
        if (value is not JsonArray links || links.Count > 1000) throw new ApiException(400, "Conceptual links must be an array with at most 1000 entries.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in links)
        {
            if (item is not JsonObject link || link.Any(pair => pair.Key is not ("id" or "targetId" or "relation" or "label")))
                throw new ApiException(400, "A conceptual link contains only id, targetId, relation and optional label.");
            var id = Text(link, "id", 96); var targetId = Text(link, "targetId", 96); var relation = Text(link, "relation", 32);
            if (!IdPattern().IsMatch(id) || !IdPattern().IsMatch(targetId) || !ids.Add(id)
                || !NodeSystems.LinkRelations.Contains(relation) || !targets.Add(targetId + ":" + relation))
                throw new ApiException(400, "Conceptual links need unique bounded identities and a supported relationship; duplicate target relationships are not allowed.");
            if (link.TryGetPropertyValue("label", out var label)
                && (label is not JsonValue labelValue || !labelValue.TryGetValue<string>(out var labelText)
                    || labelText.Length > 160 || labelText.Any(character => char.IsControl(character) && character is not ('\t' or '\r' or '\n'))))
                throw new ApiException(400, "Conceptual link labels must be text with at most 160 characters.");
        }
    }

    private static void ValidateAttachmentsAndHyperlinks(JsonObject node)
    {
        if (node.TryGetPropertyValue("attachments", out var attachmentValue))
        {
            if (attachmentValue is not JsonArray attachments || attachments.Count > 256) throw new ApiException(400, "Additional attachments must be an array with at most 256 asset identifiers.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in attachments)
                if (item is not JsonValue value || !value.TryGetValue<string>(out var id) || !IdPattern().IsMatch(id) || !seen.Add(id))
                    throw new ApiException(400, "Additional attachments must contain unique bounded asset identifiers.");
        }
        if (node.TryGetPropertyValue("hyperlinks", out var hyperlinkValue))
        {
            if (hyperlinkValue is not JsonArray hyperlinks || hyperlinks.Count > 256) throw new ApiException(400, "Hyperlinks must be an array with at most 256 entries.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in hyperlinks)
            {
                if (item is not JsonObject link || link.Any(pair => pair.Key is not ("url" or "label"))) throw new ApiException(400, "A hyperlink contains only url and optional label.");
                var urlText = Text(link, "url", 2048);
                if (urlText.Any(character => character <= ' ' || character == '\u007f') || !seen.Add(urlText)
                    || !(urlText.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || urlText.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || urlText.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                    || !Uri.TryCreate(urlText, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "mailto")
                    || (uri.Scheme == "mailto" ? string.IsNullOrEmpty(urlText[7..].Split('?')[0]) : string.IsNullOrEmpty(uri.Host)))
                    throw new ApiException(400, "Hyperlinks must use unique HTTP, HTTPS or mailto URLs.");
                if (link.TryGetPropertyValue("label", out var label)
                    && (label is not JsonValue labelValue || !labelValue.TryGetValue<string>(out var labelText)
                        || labelText.Length > 160 || labelText.Any(character => char.IsControl(character) && character is not ('\t' or '\r' or '\n'))))
                    throw new ApiException(400, "Hyperlink labels must be text with at most 160 characters.");
            }
        }
    }

    private static void ValidateNodeHub(JsonObject node)
    {
        if (node.TryGetPropertyValue("showTitle", out var titleValue)
            && (titleValue is not JsonValue titleFlag || !titleFlag.TryGetValue<bool>(out _)))
            throw new ApiException(400, "Node title visibility must be Boolean.");
        if (!node.TryGetPropertyValue("contentItems", out var value)) return;
        if (value is not JsonArray items || items.Count > 1000)
            throw new ApiException(400, "Node Hub content must be an array with at most 1000 entries.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in items)
        {
            if (entry is not JsonObject item) throw new ApiException(400, "A Node Hub item must be an object.");
            var id = Text(item, "id", 96); var type = Text(item, "type", 16);
            if (!IdPattern().IsMatch(id) || !ids.Add(id)) throw new ApiException(400, "Node Hub item identifiers must be unique bounded identifiers.");
            string[] allowed = type switch { "text" => ["id", "type", "text", "textRuns"], "asset" => ["id", "type", "assetId"], "link" => ["id", "type", "url", "label"], _ => [] };
            if (allowed.Length == 0 || item.Any(pair => !allowed.Contains(pair.Key))) throw new ApiException(400, "Node Hub items contain only supported inert text, asset or hyperlink fields.");
            if (type == "text")
            {
                if (item["text"] is not JsonValue textValue || !textValue.TryGetValue<string>(out var text) || text.Length > 1000000)
                    throw new ApiException(400, "Node Hub text must contain at most 1000000 characters.");
                ValidateTextRuns(item);
            }
            else if (type == "asset")
            {
                if (!IdPattern().IsMatch(Text(item, "assetId", 96))) throw new ApiException(400, "A Node Hub asset needs a bounded asset identifier.");
            }
            else
            {
                var link = new JsonObject { ["url"] = item["url"]?.DeepClone() };
                if (item.ContainsKey("label")) link["label"] = item["label"]?.DeepClone();
                ValidateAttachmentsAndHyperlinks(new JsonObject { ["hyperlinks"] = new JsonArray(link) });
            }
        }
    }

    private static void ValidateCanvasTarget(JsonObject node)
    {
        if (!node.TryGetPropertyValue("canvasTarget", out var value)) return;
        if (node["kind"] is not JsonValue kind || !kind.TryGetValue<string>(out var kindText) || kindText != "canvas-link"
            || node.ContainsKey("targetId"))
            throw new ApiException(400, "Only Work launchers without a local target may have an external Work destination.");
        if (value is not JsonObject target || target.Any(pair => pair.Key is not ("projectId" or "canvasId")))
            throw new ApiException(400, "Work destinations contain only projectId and optional canvasId.");
        foreach (var key in target.ContainsKey("canvasId") ? new[] { "projectId", "canvasId" } : new[] { "projectId" })
        {
            var identifier = Text(target, key, 96);
            if (identifier != identifier.Trim() || !IdPattern().IsMatch(identifier))
                throw new ApiException(400, "Work destinations must contain valid project and Work identifiers.");
        }
    }

    private static void ValidateDependencies(JsonObject node)
    {
        if (!node.TryGetPropertyValue("dependencyIds", out var value)) return;
        if (value is not JsonArray dependencies || dependencies.Count > 1000)
            throw new ApiException(400, "Node dependencies must be an array with at most 1000 identifiers.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ownId = node["id"] is JsonValue ownValue && ownValue.TryGetValue<string>(out var identifierText) ? identifierText : null;
        foreach (var item in dependencies)
        {
            if (item is not JsonValue identifier || !identifier.TryGetValue<string>(out var id)
                || !IdPattern().IsMatch(id)
                || id == ownId || !seen.Add(id))
                throw new ApiException(400, "Node dependencies must be unique identifiers of 1 to 96 letters, digits, underscores or hyphens, starting with a letter or digit and excluding the node itself.");
        }
    }

    private static void ValidateSnapping(JsonObject owner)
    {
        if (!owner.TryGetPropertyValue("snapping", out var value)) return;
        if (value is not JsonObject settings)
            throw new ApiException(400, "Snapping settings must be an object.");
        foreach (var name in new[] { "stickyEdges", "snapToGrid", "smartAlign" })
            if (settings.TryGetPropertyValue(name, out var setting)
                && (setting is not JsonValue flag || !flag.TryGetValue<bool>(out _)))
                throw new ApiException(400, $"Snapping '{name}' must be Boolean.");
    }

    // Must match src/core/fonts.ts. These are local-family identifiers, never author-supplied CSS.
    private static readonly HashSet<string> FontFamilies = new(StringComparer.Ordinal)
    {
        "sans", "serif", "mono", "arial", "arial-narrow", "arial-rounded-mt-bold",
        "aptos", "aptos-display", "aptos-narrow", "calibri", "candara", "century-gothic",
        "corbel", "bahnschrift", "franklin-gothic-medium", "franklin-gothic-book", "gill-sans-mt", "helvetica",
        "inter", "lucida-sans", "lucida-sans-unicode", "microsoft-sans-serif", "segoe-ui", "segoe-ui-variable",
        "segoe-ui-light", "segoe-ui-semibold", "tahoma", "trebuchet-ms", "verdana", "eras-light-itc",
        "eras-medium-itc", "eras-demi-itc", "eras-bold-itc", "tw-cen-mt", "times-new-roman", "georgia",
        "cambria", "constantia", "palatino-linotype", "book-antiqua", "bookman-old-style", "century",
        "century-schoolbook", "garamond", "perpetua", "goudy-old-style", "baskerville", "baskerville-old-face",
        "rockwell", "rockwell-extra-bold", "calisto-mt", "bell-mt", "bodoni-mt", "didot",
        "elephant", "high-tower-text", "imprint-mt-shadow", "footlight-mt-light", "lucida-bright", "consolas",
        "courier-new", "cascadia-code", "cascadia-mono", "lucida-console", "lucida-sans-typewriter", "andale-mono",
        "arial-black", "impact", "copperplate-gothic-bold", "copperplate-gothic-light", "britannic-bold", "broadway",
        "algerian", "castellar", "stencil", "engravers-mt", "felix-titling", "showcard-gothic",
        "bernard-mt-condensed", "bauhaus-93", "segoe-print", "segoe-script", "comic-sans-ms", "bradley-hand-itc",
        "brush-script-mt", "freestyle-script", "french-script-mt", "lucida-handwriting", "lucida-calligraphy", "monotype-corsiva",
        "mistral", "papyrus", "pristina", "vladimir-script", "malgun-gothic", "meiryo",
        "yu-gothic", "yu-mincho", "ms-gothic", "ms-mincho", "microsoft-yahei", "microsoft-jhenghei",
        "simsun", "dengxian", "nirmala-ui", "traditional-arabic"
    };

    private static void ValidateAppearance(JsonObject owner, string kind)
    {
        if (!owner.TryGetPropertyValue("style", out var value)) return;
        if (value is not JsonObject style) throw new ApiException(400, "Appearance settings must be an object.");
        string[] allowed = kind switch
        {
            "canvas" => ["background", "grid", "gridVisible", "portalStyle", "portalColor", "backgroundPattern", "backgroundScale", "backgroundTint", "backgroundAssetId", "backgroundFit", "backgroundOpacity"],
            "link" => ["color", "width", "lineStyle", "dashed"],
            _ => ["fill", "border", "textColor", "accent", "fontFamily", "fontSize", "titleSize", "textRotation", "titleRotation", "contentSizing", "textLayout", "portalStyle", "portalColor", "backgroundDisplay", "textAlign", "variant", "imageFit", "borderWidth", "borderStyle", "radius", "shape", "padding", "contentGap", "textMediaGap", "childPadding", "backgroundPattern", "backgroundScale", "backgroundTint", "backgroundAssetId", "backgroundFit", "backgroundOpacity"]
        };
        foreach (var pair in style)
        {
            if (!allowed.Contains(pair.Key)) throw new ApiException(400, $"Unknown {(kind == "canvas" ? "Work" : kind)} appearance setting '{pair.Key}'.");
            if (pair.Value is not JsonValue setting) throw new ApiException(400, $"Appearance '{pair.Key}' has an invalid value.");
            var valid = pair.Key switch
            {
                "background" or "grid" or "color" or "fill" or "border" or "textColor" or "accent" or "backgroundTint" or "portalColor" =>
                    setting.TryGetValue<string>(out var color) && ColorPattern().IsMatch(color),
                "gridVisible" or "dashed" => setting.TryGetValue<bool>(out _),
                "lineStyle" => setting.TryGetValue<string>(out var lineStyle) && lineStyle is "solid" or "dashed" or "dotted",
                "fontFamily" => setting.TryGetValue<string>(out var family) && FontFamilies.Contains(family),
                "contentSizing" => setting.TryGetValue<string>(out var contentSizing) && contentSizing is "fit" or "fixed",
                "textLayout" => setting.TryGetValue<string>(out var textLayout) && textLayout == "balanced",
                "portalStyle" => setting.TryGetValue<string>(out var portalStyle) && portalStyle is "doorway" or "passage" or "wormhole" or "vortex",
                "backgroundDisplay" => setting.TryGetValue<string>(out var backgroundDisplay) && backgroundDisplay is "always" or "hover",
                "textAlign" => setting.TryGetValue<string>(out var alignment) && alignment is "left" or "center" or "right",
                "variant" => setting.TryGetValue<string>(out var variant) && variant is "card" or "label",
                "shape" => setting.TryGetValue<string>(out var shape) && shape is "rectangle" or "square" or "rounded" or "circle" or "ellipse" or "triangle" or "diamond" or "pentagon" or "hexagon" or "octagon" or "none",
                "imageFit" => setting.TryGetValue<string>(out var fit) && fit is "contain" or "cover",
                "borderStyle" => setting.TryGetValue<string>(out var borderStyle) && borderStyle is "solid" or "dashed" or "dotted" or "double",
                "backgroundPattern" => setting.TryGetValue<string>(out var pattern) && pattern is "none" or "dots" or "grid" or "isometric" or "linen" or "contours" or "aurora" or "stars",
                "backgroundFit" => setting.TryGetValue<string>(out var backgroundFit) && backgroundFit is "cover" or "contain" or "tile",
                "backgroundAssetId" => setting.TryGetValue<string>(out var backgroundId) && IdPattern().IsMatch(backgroundId),
                "backgroundScale" => StyleNumber(setting, 8, 512),
                "backgroundOpacity" => StyleNumber(setting, 0, 1),
                "fontSize" => StyleNumber(setting, 10, 72),
                "titleSize" => StyleNumber(setting, 10, 96),
                "textRotation" or "titleRotation" => StyleNumber(setting, -180, 180),
                "borderWidth" => StyleNumber(setting, 0, 8),
                "radius" => StyleNumber(setting, 0, 48),
                "padding" or "contentGap" or "textMediaGap" or "childPadding" => StyleNumber(setting, 0, 1000),
                "width" => StyleNumber(setting, 1, 32),
                _ => false
            };
            if (!valid) throw new ApiException(400, $"Appearance '{pair.Key}' has an invalid value or range.");
        }
    }

    private static bool StyleNumber(JsonValue setting, double minimum, double maximum) =>
        setting.TryGetValue<double>(out var number) && double.IsFinite(number) && number >= minimum && number <= maximum;

    public static IEnumerable<JsonObject> Assets(JsonObject project)
    {
        foreach (var asset in (JsonArray)project["assets"]!)
            yield return asset as JsonObject ?? throw new ApiException(400, "Invalid asset entry.");
        foreach (var snapshot in (JsonObject)project["snapshots"]!)
            if (snapshot.Value is JsonObject value)
            {
                foreach (var name in new[] { "asset", "backgroundAsset" })
                    if (value[name] is JsonObject asset) yield return asset;
                if (value["assets"] is JsonArray extraAssets)
                    foreach (var item in extraAssets) yield return item as JsonObject ?? throw new ApiException(400, "Invalid saved Hub asset entry.");
            }
    }

    private static void ValidateBackgroundImage(JsonObject owner, Dictionary<string, JsonObject> assets, JsonObject? pinned = null)
    {
        if (owner["style"] is not JsonObject style || style["backgroundAssetId"] is not JsonValue idValue
            || !idValue.TryGetValue<string>(out var id)) return;
        var asset = pinned ?? assets.GetValueOrDefault(id);
        if (asset is null) return; // Missing local media remains repairable, as with ordinary attachments.
        var mime = Text(asset, "mime", 128);
        var bytes = AssetBytes(asset);
        bool Starts(params byte[] prefix) => bytes.AsSpan().StartsWith(prefix);
        var valid = mime switch
        {
            "image/png" => Starts(137, 80, 78, 71, 13, 10, 26, 10),
            "image/jpeg" => Starts(255, 216, 255),
            "image/gif" => Starts(71, 73, 70, 56, 55, 97) || Starts(71, 73, 70, 56, 57, 97),
            "image/webp" => bytes.Length >= 12 && Starts(82, 73, 70, 70) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            "image/bmp" => Starts(66, 77),
            "image/avif" => bytes.Length >= 12 && bytes.AsSpan(4, 4).SequenceEqual("ftyp"u8)
                && (System.Text.Encoding.ASCII.GetString(bytes.AsSpan(8, Math.Min(bytes.Length - 8, 56))).Contains("avif", StringComparison.Ordinal)
                    || System.Text.Encoding.ASCII.GetString(bytes.AsSpan(8, Math.Min(bytes.Length - 8, 56))).Contains("avis", StringComparison.Ordinal)),
            _ => false
        };
        if (!valid || Text(asset, "base64", int.MaxValue) != Convert.ToBase64String(bytes))
            throw new ApiException(400, "Background images must contain a supported local raster image.");
    }

    public static byte[] AssetBytes(JsonObject asset)
    {
        if (asset["base64"] is not JsonValue node || !node.TryGetValue<string>(out var encoded)
            || encoded.Length > (MaxAssetBytes + 2L) / 3 * 4)
            throw new ApiException(413, "An attachment is invalid or exceeds 16 MiB.");
        try
        {
            var bytes = Convert.FromBase64String(encoded);
            if (bytes.Length > MaxAssetBytes) throw new ApiException(413, "An attachment exceeds 16 MiB.");
            return bytes;
        }
        catch (FormatException) { throw new ApiException(400, "An attachment contains invalid base64 data."); }
    }

    private static JsonArray Array(JsonObject project, string name, int maximum)
    {
        if (project[name] is not JsonArray array || array.Count > maximum)
            throw new ApiException(400, $"'{name}' must be an array with at most {maximum} entries.");
        return array;
    }
    private static HashSet<string> UniqueIds(JsonArray entries, string kind)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in entries)
            if (value is not JsonObject item || !ids.Add(Text(item, "id", 96)))
                throw new ApiException(400, $"Invalid or duplicate {kind} identifier.");
        return ids;
    }
}
