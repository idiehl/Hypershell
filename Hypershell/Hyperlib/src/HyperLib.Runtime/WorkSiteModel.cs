using HyperLib.Core;
using System.Text.Json;

namespace HyperLib.Runtime;

/// <summary>A reading view of one Work, derived without changing library content.</summary>
public sealed class WorkSiteModel
{
    public required LibraryNode Work { get; init; }
    public required LibraryNode Current { get; init; }
    public LibraryNode? LibraryTarget { get; init; }
    public required IReadOnlyList<WorkContentsItem> Contents { get; init; }
    public required IReadOnlyList<WorkContentsItem> CurrentContents { get; init; }
    public required IReadOnlyList<LibraryNode> Pages { get; init; }
    public LibraryNode? Previous { get; init; }
    public LibraryNode? Next { get; init; }
    public LibraryNode? Start => Flatten(CurrentContents).FirstOrDefault(item => item.Node.Type == "page")?.Node;

    public static WorkSiteModel? Create(LibraryState? state, LibraryNode? current)
    {
        if (state is null || current is null) return null;
        var byId = state.Nodes.GroupBy(node => node.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var work = current;
        while (work.Type != "work" && visited.Add(work.Id))
        {
            if (work.ParentId is null || !byId.TryGetValue(work.ParentId, out var parent))
            {
                work = current.WorkId is { } id && byId.TryGetValue(id, out var owner) && owner.Type == "work" ? owner : null;
                break;
            }
            work = parent;
        }
        if (work?.Type != "work") return null;
        var children = state.Nodes.Where(node => node.ParentId is not null)
            .GroupBy(node => node.ParentId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => group.OrderBy(node => node.Order).ThenBy(node => node.Title, StringComparer.Ordinal)
                    .ThenBy(node => node.Id, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var included = new HashSet<string>(StringComparer.Ordinal) { work.Id };
        IReadOnlyList<WorkContentsItem> Build(string parentId)
        {
            if (!children.TryGetValue(parentId, out var nodes)) return [];
            var entries = new List<WorkContentsItem>();
            foreach (var node in nodes)
            {
                if (node.Type == "work" || !included.Add(node.Id)
                    || node.Type == "page" && node.WorkId is { Length: > 0 } owner && owner != work.Id) continue;
                var nested = node.Type == "page" ? [] : Build(node.Id);
                if (node.Type == "page" || nested.Count > 0) entries.Add(new(node, nested));
            }
            return entries;
        }
        var contents = Build(work.Id);
        var pages = Flatten(contents).Where(item => item.Node.Type == "page").Select(item => item.Node).ToArray();
        var selected = Flatten(contents).FirstOrDefault(item => item.Node.Id == current.Id);
        var currentContents = current.Id == work.Id ? contents : selected?.Children ?? [];
        var index = Array.FindIndex(pages, page => page.Id == current.Id);
        LibraryNode? target = null;
        var ancestor = work;
        visited.Clear();
        while (ancestor.ParentId is { } parentId && visited.Add(parentId) && byId.TryGetValue(parentId, out var parent))
        {
            if (parent.Type == "collection") { target = parent; break; }
            target = parent;
            ancestor = parent;
        }
        return new()
        {
            Work = work, Current = current, LibraryTarget = target,
            Contents = contents, CurrentContents = currentContents, Pages = pages,
            Previous = index > 0 ? pages[index - 1] : null,
            Next = index >= 0 && index + 1 < pages.Length ? pages[index + 1] : null
        };
    }

    public static IEnumerable<WorkContentsItem> Flatten(IEnumerable<WorkContentsItem> entries)
    {
        foreach (var entry in entries)
        {
            yield return entry;
            foreach (var child in Flatten(entry.Children)) yield return child;
        }
    }

    public static string Href(RendererOptions options, LibraryNode node) =>
        options.PathBase.TrimEnd('/') + (node.Type == "page" ? "/page/" : "/browse/") + Uri.EscapeDataString(node.Id);

    public static string LibraryHref(RendererOptions options, LibraryNode node) =>
        new Uri(options.GatewayUrl).AbsolutePath.TrimEnd('/') + "/#" + Uri.EscapeDataString(node.Id);

    public string Description(LibraryState state, RendererOptions options)
    {
        foreach (var node in new[] { Current, Work }.DistinctBy(node => node.Id))
            foreach (var name in new[] { "description", "subtitle" })
                if (node.Appearance.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } text) return text;
        if (Start is { } start && state.Pages.TryGetValue(start.Id, out var page))
            foreach (var block in page.Blocks)
                if (options.Template.Blocks.FirstOrDefault(definition => definition.Id == block.Type)?.Component.Split('.').Last()
                    is "HeaderBlock" or "BannerBlock"
                    && block.Values.TryGetValue("text", out var value) && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } text) return text;
        var count = Flatten(CurrentContents).Count(item => item.Node.Type == "page");
        return count == 1 ? "One page to explore." : count > 1 ? $"{count} pages to explore." : "New pages will appear here.";
    }

    public string? Cover(LibraryState state, RendererOptions options)
    {
        var cover = Current.Cover ?? Work.Cover;
        if (!string.IsNullOrWhiteSpace(cover) && TemplateValidator.IsSafeUrl(cover)) return cover;
        if (Start is { } start && state.Pages.TryGetValue(start.Id, out var page))
            foreach (var block in page.Blocks)
                if (options.Template.Blocks.FirstOrDefault(definition => definition.Id == block.Type)?.Component.Split('.').Last()
                    is "ImageBlock" or "BannerBlock"
                    && block.Values.TryGetValue("src", out var value) && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } src && TemplateValidator.IsSafeUrl(src)) return src;
        return null;
    }
}

public sealed record WorkContentsItem(LibraryNode Node, IReadOnlyList<WorkContentsItem> Children);
