using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Models;

namespace Hypershell.Services;

public sealed record StudioWebProjectBlueprint(
    IReadOnlyList<HyperpagePageCreationDefinition> Pages,
    IReadOnlyDictionary<string, string> AssetSources,
    IReadOnlyDictionary<string, string> TextAssets,
    string Css,
    string JavaScript,
    IReadOnlyList<VisualPageTemplateDocument> ReusableTemplates);

/// <summary>Ports native template trees and the shared block wizard into editable HTML, without freezing server tools into pictures or inert markup.</summary>
public sealed class StudioWebProjectTemplateAdapter(VisualPageComponentCatalog catalog, string? libraryRoot)
{
    private readonly VisualPageTemplateService _templates = new(catalog);
    private readonly Dictionary<string, string> _assets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _textAssets = new(StringComparer.Ordinal);
    private readonly StringBuilder _css = new();
    private IReadOnlyList<VisualPageTemplateDocument>? _available;
    private IReadOnlyList<(string Name, string Route)> _routes = [];
    private string _currentRoute = "/";

    public void ValidateWork(HyperlibWorkProjectDefinition work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var recipes = new[] { work.LandingTemplate }.Concat(work.PageTemplates.Count > 0
            ? work.PageTemplates.Select(item => item.Definition) : [work.PageTemplate]);
        foreach (var recipe in recipes)
        {
            if (!string.IsNullOrWhiteSpace(recipe.BackgroundTemplateId)) throw Unsupported(recipe.BackgroundTemplateId, "A library palette");
            var ids = new[] { recipe.LayoutTemplateId, recipe.BodyLayoutTemplateId, recipe.HeaderTemplateId, recipe.FooterTemplateId, recipe.NavigationTemplateId, recipe.BodyTemplateId }
                .Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!)
                .Concat(recipe.RepeatingObjects.Select(item => item.TemplateObjectId))
                .Concat(new[] { recipe.HeaderLayout, recipe.NavigationLayout, recipe.BodyLayout, recipe.FooterLayout }
                    .SelectMany(HyperlibHeaderLayouts.Elements).Where(item => item.Kind == "template").Select(item => item.TemplateObjectId));
            foreach (var id in ids.Distinct(StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(libraryRoot)) throw Unsupported(id, "This library object");
                var separator = id.IndexOf(':');
                if (separator < 1) throw Unsupported(id, "This library object");
                var item = new HyperlibWorkProjectService(libraryRoot).GetCatalogObject(id[..separator], id[(separator + 1)..]);
                if (!_templates.BuiltInTemplates.Any(template => template.TemplateId == item.Element?.SourceTemplateId))
                    throw Unsupported(id, item.Title);
            }
        }
    }

    public async Task<StudioWebProjectBlueprint> BuildAsync(string name, StudioProjectKind kind,
        HyperlibWorkProjectDefinition? work, string? completeTemplateId, CancellationToken ct = default)
    {
        if (kind is not (StudioProjectKind.Html or StudioProjectKind.Razor))
            throw new VisualPageValidationException("Choose HTML or Razor Pages for a web project.");
        _assets.Clear(); _textAssets.Clear(); _css.Clear();
        var personal = await _templates.LoadPersonalAsync(ct);
        _available = _templates.BuiltInTemplates.Concat(personal.Templates).ToArray();
        var pages = new List<HyperpagePageCreationDefinition>();
        var reusable = new List<VisualPageTemplateDocument>();
        if (!string.IsNullOrWhiteSpace(completeTemplateId))
        {
            if (!completeTemplateId.StartsWith("visual:", StringComparison.Ordinal))
                throw new VisualPageValidationException("This complete template uses Hyperlib's server components. Choose Hyperlib to keep its tools working, or choose a browser-compatible page template for HTML or Razor Pages.");
            var source = _available.SingleOrDefault(item => item.TemplateId == completeTemplateId[7..] && item.Scope == "page")
                ?? throw new VisualPageValidationException("The selected complete page template is no longer available. Choose it again.");
            var template = await PrepareCompleteAsync(source, name, ct);
            pages.Add(new() { Name = "Home", Route = "/", Template = template });
            reusable.Add(Clone(template));
        }
        else
        {
            if (work is null || work.PipelineVersion != 2)
                throw new VisualPageValidationException("Finish the site builder before creating this project.");
            ValidateWork(work);
            var named = work.PageTemplates.Count > 0 ? work.PageTemplates :
                [new HyperlibNamedPageTemplateDefinition { Id = "page-default", Name = "Content Page", Definition = work.PageTemplate }];
            var chosen = named.FirstOrDefault(item => item.Id == work.DefaultPageTemplateId) ?? named[0];
            _routes = [(name, "/"), (chosen.Name, "/page-1/")];
            _currentRoute = "/";
            var home = await BuildRecipeAsync(work.LandingTemplate, name, "studio-landing", ct);
            pages.Add(new() { Name = "Home", Route = "/", Template = home });
            for (var index = 0; index < named.Count; index++)
            {
                _currentRoute = "/page-1/";
                var template = await BuildRecipeAsync(named[index].Definition, named[index].Name,
                    "studio-page-" + index.ToString(CultureInfo.InvariantCulture), ct);
                template.TemplateId = "studio-wizard-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(named[index].Id)));
                template.Name = named[index].Name;
                template.SuggestedPageName = named[index].Name;
                template.SuggestedPageRoute = "";
                reusable.Add(template);
                if (named[index].Id == chosen.Id)
                    pages.Add(new() { Name = "Page 1", Route = "/page-1/", Template = Clone(template) });
            }
        }
        foreach (var page in pages) EnsureBrowserCompatible(page.Template!, kind);
        foreach (var template in reusable) EnsureBrowserCompatible(template, kind);
        return new(pages, new Dictionary<string, string>(_assets), new Dictionary<string, string>(_textAssets),
            BaseCss + "\n" + _css, NavigationJavaScript, reusable);
    }

    private async Task<VisualPageTemplateDocument> PrepareCompleteAsync(VisualPageTemplateDocument source, string name, CancellationToken ct)
    {
        var result = Clone(source);
        result.StorageScope = "project";
        result.SuggestedPageName = "Home"; result.SuggestedPageRoute = "/";
        result.SuggestedPageTitle = name;
        await PrepareAssetsAsync(result, source, ct);
        return result;
    }

    private async Task PrepareAssetsAsync(VisualPageTemplateDocument target, VisualPageTemplateDocument source, CancellationToken ct)
    {
        var mapping = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var asset in source.AssetIds)
        {
            ct.ThrowIfCancellationRequested();
            mapping[asset] = source.PackagedAssets.ContainsKey(asset)
                ? ImportAsset(_templates.ResolvePackagedAssetPath(source, asset))
                : Placeholder();
        }
        foreach (var element in Flatten(target.Root))
        {
            foreach (var attribute in element.Attributes.ToList())
            {
                var value = attribute.Value;
                foreach (var item in mapping) value = value.Replace("asset:" + item.Key, item.Value, StringComparison.Ordinal);
                if (attribute.Key is "src" or "poster") value = ResolveMedia(value);
                element.Attributes[attribute.Key] = value;
            }
            foreach (var style in element.Styles.ToList())
            {
                var value = style.Value;
                foreach (var item in mapping) value = value.Replace("asset:" + item.Key, item.Value, StringComparison.Ordinal);
                element.Styles[style.Key] = value;
            }
        }
        target.AssetIds.Clear(); target.PackagedAssets.Clear();
        await Task.CompletedTask;
    }

    private async Task<VisualPageTemplateDocument> BuildRecipeAsync(HyperlibPageTemplateDefinition recipe, string title, string scope, CancellationToken ct)
    {
        var root = Element("main", title, attributes: [("class", "studio-wizard-site " + scope), ("data-studio-page-template", scope)]);
        root.DescriptorId = "core.page";
        root.Styles["min-height"] = "100vh";
        var surface = Clone(recipe.BackgroundSurface);
        if (!string.IsNullOrWhiteSpace(surface.ImagePath))
        {
            var imported = Path.IsPathFullyQualified(surface.ImagePath) && !surface.ImagePath.StartsWith("/", StringComparison.Ordinal)
                ? ImportAsset(surface.ImagePath, "media/") : ResolveMedia(surface.ImagePath);
            // Shared validation requires /media URLs; the copy keeps that directory inside the new web project.
            surface.ImagePath = imported;
        }
        _css.Append('.').Append(scope).Append('{').Append(HyperlibBackgroundDesigns.SurfaceCss(recipe.BackgroundColor, surface)).AppendLine("}");
        if (!string.IsNullOrWhiteSpace(recipe.BackgroundTemplateId))
            throw Unsupported(recipe.BackgroundTemplateId, "A library palette");
        if (!string.IsNullOrWhiteSpace(recipe.LayoutTemplateId))
            root.Children.Add(await LibraryElementAsync(recipe.LayoutTemplateId, null, ct));
        var header = await RegionAsync("header", recipe.HeaderLayout, recipe.HeaderStyle, recipe.HeaderTemplateId, ct);
        if (recipe.HeaderPlacement == "top") root.Children.Add(header);
        VisualPageElement? navigation = null;
        if (recipe.IncludeNavigation && recipe.NavigationPlacement != "none")
        {
            navigation = await RegionAsync("nav", recipe.NavigationLayout, recipe.NavigationStyle, recipe.NavigationTemplateId, ct);
            navigation.Name = "Page navigation";
            if (recipe.NavigationSticky) { navigation.Styles["position"] = "sticky"; navigation.Styles["top"] = "0"; navigation.Styles["z-index"] = "10"; }
            if (!recipe.NavigationLayout.Enabled && string.IsNullOrWhiteSpace(recipe.NavigationTemplateId))
                navigation.Children.Add(Navigation(new HyperlibNavigationDefinition(), "Default page navigation"));
            if (recipe.NavigationPlacement == "top") root.Children.Add(navigation);
        }
        var body = await RegionAsync("section", recipe.BodyLayout, recipe.BodyStyle,
            recipe.BodyLayoutTemplateId ?? recipe.BodyTemplateId, ct);
        body.Name = "Page body";
        var footer = await RegionAsync("footer", recipe.FooterLayout, recipe.FooterStyle, recipe.FooterTemplateId, ct);
        var aside = Element("aside", "Additional page elements");
        foreach (var extra in recipe.RepeatingObjects.OrderBy(item => item.Order))
        {
            if (extra.Region == "main" && recipe.BodyLayout.Enabled || extra.Region == "footer" && recipe.FooterLayout.Enabled) continue;
            var target = extra.Region == "header" ? header : extra.Region == "footer" ? footer : extra.Region == "sidebar" ? aside : body;
            target.Children.Add(await LibraryElementAsync(extra.TemplateObjectId, null, ct));
        }
        if (navigation is not null && recipe.NavigationPlacement is "left" or "right")
        {
            var content = Element("div", "Body and side navigation", attributes: [("class", "studio-content-layout")]);
            content.Styles["display"] = "grid";
            content.Styles["grid-template-columns"] = recipe.NavigationPlacement == "right" ? "minmax(0,1fr) minmax(180px,28%)" : "minmax(180px,28%) minmax(0,1fr)";
            content.Styles["align-items"] = "start";
            if (recipe.NavigationPlacement == "left") content.Children.Add(navigation);
            content.Children.Add(body);
            if (recipe.NavigationPlacement == "right") content.Children.Add(navigation);
            root.Children.Add(content);
        }
        else root.Children.Add(body);
        if (aside.Children.Count > 0) root.Children.Add(aside);
        if (navigation is not null && recipe.NavigationPlacement == "bottom") root.Children.Add(navigation);
        if (recipe.FooterPlacement == "bottom") root.Children.Add(footer);
        var width = recipe.ContentWidth == "comfortable" ? "72rem" : recipe.ContentWidth == "wide" ? "92rem" : "none";
        foreach (var region in root.Children)
        {
            region.Styles["width"] = "100%";
            region.Styles["max-width"] = width;
            region.Styles["margin-left"] = recipe.ContentAlignment == "left" ? "0" : "auto";
            region.Styles["margin-right"] = recipe.ContentAlignment == "right" ? "0" : "auto";
        }
        return new() { TemplateId = scope, Name = title, Scope = "page", StorageScope = "project", Library = "Project page layouts",
            CategoryGroup = "Layout & structure", Category = "Page layouts", Description = "Reusable page layout created in the site builder.",
            SuggestedPageTitle = title, Root = root, ThemeTokens = new(StringComparer.Ordinal)
            { ["color.background"] = recipe.BackgroundColor, ["color.accent"] = recipe.AccentColor, ["color.text"] = "#f2f5f4" } };
    }

    private async Task<VisualPageElement> RegionAsync(string tag, HyperlibHeaderLayoutDefinition layout, HyperlibTemplateRegionStyle style,
        string? fallback, CancellationToken ct)
    {
        var region = Element(tag, tag == "section" ? "Body" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(tag));
        region.Attributes["class"] = "studio-wizard-region";
        ApplyStyles(region, "padding:" + N(style.Padding) + "px;border:" + N(style.BorderWidth) + "px solid " + (string.IsNullOrEmpty(style.BorderColor) ? "transparent" : style.BorderColor)
            + ";border-radius:" + N(style.BorderRadius) + "px;text-align:" + style.Alignment + ";font-size:" + N(style.FontSize) + "px;font-family:" + style.FontFamily + ";");
        if (!string.IsNullOrEmpty(style.BackgroundColor)) region.Styles["background-color"] = style.BackgroundColor;
        if (!string.IsNullOrEmpty(style.TextColor)) region.Styles["color"] = style.TextColor;
        if (layout.Enabled)
        {
            HyperlibHeaderLayouts.Validate(layout, allowLocalMedia: true);
            foreach (var row in layout.Blocks)
            {
                ct.ThrowIfCancellationRequested();
                var outer = Element("div", "Block", attributes: [("class", "studio-header-row"), ("data-header-row", row.Id)]);
                ApplyStyles(outer, HyperlibHeaderLayouts.RowStyle(row));
                var content = Element("div", "Block contents", attributes: [("class", "studio-header-row-content")]);
                ApplyStyles(content, HyperlibHeaderLayouts.ContainerStyle(row.Arrangement));
                foreach (var child in row.Elements) content.Children.Add(await WizardElementAsync(child, row.Arrangement, ct));
                outer.Children.Add(content); region.Children.Add(outer);
            }
        }
        else if (!string.IsNullOrWhiteSpace(fallback)) region.Children.Add(await LibraryElementAsync(fallback, null, ct));
        if (!layout.Enabled)
        {
            if (!string.IsNullOrWhiteSpace(style.LogoPath))
            {
                var logo = Element("img", "Region logo", attributes: [("src", ResolveMedia(style.LogoPath)), ("alt", "Site logo")]);
                logo.Styles["max-width"] = "160px"; logo.Styles["max-height"] = "80px";
                region.Children.Insert(0, logo);
            }
            if (!string.IsNullOrWhiteSpace(style.Text)) region.Children.Add(Element("p", "Region text", style.Text));
        }
        return region;
    }

    private async Task<VisualPageElement> WizardElementAsync(HyperlibHeaderElementDefinition source, string arrangement, CancellationToken ct)
    {
        var title = source.IsContentSlot && !string.IsNullOrWhiteSpace(source.SlotName) ? source.SlotName : source.Kind;
        var wrapper = Element("div", title, attributes: [("class", "studio-header-element studio-header-" + source.Kind), ("data-header-element", source.Id)]);
        ApplyStyles(wrapper, HyperlibHeaderLayouts.ElementStyle(source, arrangement));
        if (source.IsContentSlot)
        {
            wrapper.Attributes["data-content-slot"] = "true";
            wrapper.Attributes["data-slot-name"] = source.SlotName;
        }
        if (source.Kind == "text") wrapper.Children.Add(Element("div", title + " text", source.Text));
        else if (source.Kind == "box")
        {
            var content = Element("div", "Box contents", attributes: [("class", "studio-header-box-content")]);
            ApplyStyles(content, HyperlibHeaderLayouts.ContainerStyle(source.Arrangement));
            foreach (var child in source.Children) content.Children.Add(await WizardElementAsync(child, source.Arrangement, ct));
            wrapper.Children.Add(content);
        }
        else if (source.Kind is "image" or "video")
        {
            if (source.IsContentSlot && string.IsNullOrWhiteSpace(source.MediaPath)) return wrapper;
            var media = Element(source.Kind == "image" ? "img" : "video", title);
            if (!string.IsNullOrWhiteSpace(source.MediaPath)) media.Attributes["src"] = ResolveMedia(source.MediaPath);
            else if (source.Kind == "image") media.Attributes["src"] = Placeholder();
            if (source.Kind == "image") media.Attributes["alt"] = source.Alt;
            else { media.Attributes["controls"] = "true"; media.Attributes["playsinline"] = "true"; media.Attributes["preload"] = "metadata"; media.Attributes["aria-label"] = string.IsNullOrWhiteSpace(source.Alt) ? title : source.Alt; }
            ApplyStyles(media, HyperlibHeaderLayouts.MediaStyle(source));
            media.Styles["display"] = "block"; media.Styles["max-width"] = "100%";
            wrapper.Children.Add(media);
        }
        else if (source.Kind == "navigation") wrapper.Children.Add(Navigation(source.Navigation ?? new(), title));
        else if (source.Kind == "template") wrapper.Children.Add(await LibraryElementAsync(source.TemplateObjectId, source, ct));
        else throw new VisualPageValidationException("This web layout contains an unsupported element: " + source.Kind + ".");
        return wrapper;
    }

    private async Task<VisualPageElement> LibraryElementAsync(string choiceId, HyperlibHeaderElementDefinition? configured, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot)) throw Unsupported(choiceId, "This library object");
        var separator = choiceId.IndexOf(':');
        if (separator < 1) throw Unsupported(choiceId, "This library object");
        var service = new HyperlibWorkProjectService(libraryRoot);
        var item = service.GetCatalogObject(choiceId[..separator], choiceId[(separator + 1)..]);
        var originalId = item.Element?.SourceTemplateId;
        var original = _available!.SingleOrDefault(template => template.TemplateId == originalId);
        if (original is null) throw Unsupported(choiceId, item.Title);
        var template = Clone(original);
        template.Scope = "element"; // Repeated imports need independent DOM IDs and their matching anchor references.
        var root = VisualPageJson.Clone(template.Root);
        if (root.DescriptorId != "core.page")
        {
            for (var depth = 0; depth < 8; depth++)
            {
                var descriptor = catalog.GetRequired(root.DescriptorId);
                var allowed = descriptor.Authoring.AllowedParentMarkupNames;
                if (allowed.Count == 0 || allowed.Contains("main", StringComparer.OrdinalIgnoreCase)) break;
                var parent = catalog.Descriptors.FirstOrDefault(candidate => allowed.Contains(candidate.MarkupName, StringComparer.OrdinalIgnoreCase) && candidate.AllowsChildren && !candidate.IsDocumentOnly)
                    ?? throw Unsupported(choiceId, item.Title);
                var container = catalog.CreateElement(parent.Id); container.Children.Add(root); root = container;
            }
            var page = catalog.CreateElement("core.page"); page.Children.Add(root); root = page;
        }
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (item.Element is { } schema)
            foreach (var field in schema.Fields.Where(field => field.DefaultValue.HasValue)) values[field.Id] = field.DefaultValue!.Value.Clone();
        foreach (var block in item.Blocks) foreach (var value in block.Values) values[value.Key] = value.Value.Clone();
        if (configured is not null)
        {
            foreach (var fields in configured.TemplateValues.Values) foreach (var value in fields) values[value.Key] = value.Value.Clone();
            foreach (var block in configured.TemplateBlocks) foreach (var value in block.Values) values[value.Key] = value.Value.Clone();
        }
        var ordinal = 0;
        foreach (var element in Flatten(root))
        {
            if (values.TryGetValue("text-" + ordinal, out var text) && text.ValueKind == JsonValueKind.String) element.Text = text.GetString() ?? "";
            foreach (var attribute in element.Attributes.ToList())
                if (values.TryGetValue(attribute.Key + "-" + ordinal, out var value) && value.ValueKind == JsonValueKind.String)
                    element.Attributes[attribute.Key] = value.GetString() ?? "";
            ordinal++;
        }
        template.Root = root;
        template.Root = _templates.Instantiate(template);
        await PrepareAssetsAsync(template, original, ct);
        if (template.Root.DescriptorId == "core.page") template.Root.DescriptorId = "core.container";
        foreach (var token in original.ThemeTokens)
            template.Root.Styles[ThemeVariable(token.Key)] = token.Value;
        template.Root.Name = item.Title;
        return template.Root;
    }

    private VisualPageElement Navigation(HyperlibNavigationDefinition settings, string title)
    {
        HyperlibNavigationLayouts.Validate(settings);
        var id = "nav-" + Guid.NewGuid().ToString("N")[..12];
        var nav = Element("nav", title, attributes: [("class", "studio-web-navigation"), ("aria-label", "Page navigation"),
            ("data-auto-pages", settings.AutoPages ? "true" : "false"), ("data-page-steps", settings.ShowPreviousNext ? "true" : "false"),
            ("data-include-home", settings.IncludeHome ? "true" : "false"), ("data-home-label", settings.HomeLabel)]);
        ApplyStyles(nav, "font-family:" + settings.FontFamily + ";font-size:" + N(settings.FontSize) + "px;--nav-text:" + settings.TextColor
            + ";--nav-fill:" + (settings.BackgroundColor.Length == 0 ? "transparent" : settings.BackgroundColor) + ";--nav-hover:" + settings.HoverColor + ";--nav-active:" + settings.ActiveColor
            + ";--nav-radius:" + N(settings.Radius) + "px;--nav-padding-x:" + N(settings.LinkPaddingX) + "px;--nav-padding-y:" + N(settings.LinkPaddingY) + "px;");
        var links = Element("div", "Navigation links", attributes: [("id", id), ("class", "studio-web-navigation-links")]);
        ApplyStyles(links, "display:flex;flex-wrap:wrap;gap:" + N(settings.Gap) + "px;flex-direction:" + (settings.Orientation == "vertical" ? "column" : "row")
            + ";justify-content:" + (settings.Alignment == "center" ? "center" : settings.Alignment == "right" ? "flex-end" : "flex-start") + ";");
        void Link(string label, string href, string? relation = null, bool newTab = false, bool generated = false)
        {
            var anchor = Element("a", label + " link", label, [("href", href)]);
            if (href == _currentRoute) anchor.Attributes["aria-current"] = "page";
            if (relation is not null) anchor.Attributes["rel"] = relation;
            if (generated) anchor.Attributes["data-studio-page-link"] = "true";
            if (newTab) { anchor.Attributes["target"] = "_blank"; anchor.Attributes["rel"] = "noopener noreferrer"; }
            links.Children.Add(anchor);
        }
        if (settings.IncludeHome) Link(settings.HomeLabel, "/", generated: true);
        if (settings.AutoPages) foreach (var route in _routes.Skip(1)) Link(route.Name, route.Route, generated: true);
        foreach (var link in settings.CustomLinks)
        {
            if (link.Target.StartsWith("page:", StringComparison.Ordinal))
                throw new VisualPageValidationException("The navigation link '" + link.Label + "' refers to a Hyperlib Page ID. Use a web path such as /page-1/ for this web project.");
            Link(link.Label, link.Target, newTab: link.NewTab);
        }
        if (settings.ShowPreviousNext)
        {
            var index = _routes.ToList().FindIndex(route => route.Route == _currentRoute);
            if (index > 0) Link("← Previous Page", _routes[index - 1].Route, "prev", generated: true);
            if (index >= 0 && index + 1 < _routes.Count) Link("Next Page →", _routes[index + 1].Route, "next", generated: true);
        }
        if (settings.CollapseOnSmallScreens)
        {
            nav.Attributes["data-nav-breakpoint"] = N(settings.CollapseBreakpoint);
            nav.Children.Add(Element("button", "Navigation menu toggle", settings.MenuLabel,
                [("type", "button"), ("class", "studio-web-navigation-toggle"), ("aria-expanded", "false"), ("aria-controls", id)]));
        }
        nav.Children.Add(links); return nav;
    }

    private string ResolveMedia(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        if (path.StartsWith("asset:", StringComparison.Ordinal) || path is "assets/placeholder.svg" or "/assets/placeholder.svg" || path.Contains("/_content/HyperLib.Work/legacy/placeholder.svg", StringComparison.Ordinal)) return Placeholder();
        if (Path.IsPathFullyQualified(path) && !path.StartsWith("/", StringComparison.Ordinal)) return ImportAsset(path);
        if (path.StartsWith("/media/", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(libraryRoot)) throw new VisualPageValidationException("The selected local media source is unavailable.");
            var relative = Uri.UnescapeDataString(path[7..]);
            var mediaRoot = Path.Combine(libraryRoot, "Media");
            var source = VisualPageProjectService.ResolveProjectPath(mediaRoot, relative);
            return ImportAsset(source, "media/");
        }
        return path;
    }

    private string ImportAsset(string path, string directory = "assets/")
    {
        var info = new FileInfo(Path.GetFullPath(path));
        if (!info.Exists || info.Length > 32L * 1024 * 1024) throw new VisualPageValidationException("A selected project asset is missing or exceeds 32 MiB: " + info.Name);
        VisualPageProjectService.RejectReparsePoint(info.FullName);
        for (var parent = info.Directory; parent is not null; parent = parent.Parent) VisualPageProjectService.RejectReparsePoint(parent.FullName);
        var extension = info.Extension.ToLowerInvariant();
        if (!new[] { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".svg", ".avif", ".mp4", ".webm", ".ogg", ".mp3", ".wav", ".m4a", ".woff", ".woff2", ".ttf", ".ico", ".pdf" }.Contains(extension))
            throw new VisualPageValidationException("This template asset type is unsupported: " + info.Name);
        var target = directory + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(info.FullName)))[..24] + extension;
        _assets[target] = info.FullName;
        if (_assets.Count > 128 || _assets.Values.Distinct(StringComparer.OrdinalIgnoreCase).Sum(file => new FileInfo(file).Length) > 128L * 1024 * 1024)
            throw new VisualPageValidationException("The selected templates exceed the 128 asset / 128 MiB project-creation limit.");
        return "/" + target;
    }

    private string Placeholder()
    {
        const string path = "assets/studio-placeholder.svg";
        _textAssets[path] = "<svg xmlns='http://www.w3.org/2000/svg' width='1200' height='750' viewBox='0 0 1200 750'><rect width='1200' height='750' fill='#172845'/><circle cx='875' cy='205' r='70' fill='#edcf85'/><path d='M0 610 300 260 560 555 775 385 1200 650V750H0Z' fill='#376060'/><text x='60' y='690' font-family='system-ui,sans-serif' font-size='24' fill='#f7eedc'>Replace with your own image</text></svg>";
        return "/" + path;
    }

    private VisualPageElement Element(string tag, string name, string text = "", (string Key, string Value)[]? attributes = null)
    {
        var attrs = (attributes ?? []).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        if (!catalog.TryGetHtmlElement(tag, attrs, out var descriptor)) throw new VisualPageValidationException("The web element '" + tag + "' is unavailable.");
        return new() { DescriptorId = descriptor.Id, Name = name, Text = text, Attributes = attrs };
    }

    private static void ApplyStyles(VisualPageElement element, string css)
    {
        // Shared geometry helpers emit validated declarations and no quoted semicolons.
        foreach (var part in css.Split(';', StringSplitOptions.RemoveEmptyEntries))
        { var index = part.IndexOf(':'); if (index > 0) element.Styles[part[..index].Trim()] = part[(index + 1)..].Trim(); }
    }
    private void EnsureBrowserCompatible(VisualPageTemplateDocument template, StudioProjectKind kind)
    {
        var unsupported = Flatten(template.Root).FirstOrDefault(element => element.IsCustomCode || element.Bindings.Count > 0 || element.Events.Count > 0
            || element.DescriptorId is "core.condition" or "core.loop" || catalog.GetRequired(element.DescriptorId).MarkupName.Any(char.IsUpper));
        if (unsupported is not null) throw new VisualPageValidationException("The template '" + template.Name + "' contains server logic in '" + unsupported.Name
            + "'. Choose Hyperlib to retain that tool, or choose a browser-compatible template for " + (kind == StudioProjectKind.Html ? "HTML" : "Razor Pages") + ".");
    }
    private static VisualPageValidationException Unsupported(string id, string title) => new("'" + title + "' (" + id
        + ") uses Hyperlib server components without an editable browser equivalent. Choose Hyperlib to retain this object, or replace it with a browser-compatible object before creating HTML or Razor Pages.");
    private static VisualPageTemplateDocument Clone(VisualPageTemplateDocument value) => VisualPageJson.Clone(value);
    private static T Clone<T>(T value) where T : class => VisualPageJson.Clone(value);
    private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string ThemeVariable(string token) => "--vp-" + (token switch
    {
        "color.background" => "background", "color.surface" => "surface", "color.text" => "text", "color.muted" => "muted",
        "color.accent" => "accent", "radius.card" => "radius-card", "space.section" => "space-section",
        _ => token.Replace('.', '-').ToLowerInvariant()
    });
    private static IEnumerable<VisualPageElement> Flatten(VisualPageElement element)
    { yield return element; foreach (var child in element.Children) foreach (var item in Flatten(child)) yield return item; }

    private const string BaseCss = """
        html,body{margin:0;min-height:100%;}body{font-family:system-ui,sans-serif;background:var(--vp-background,#101820);color:var(--vp-text,#f2f5f4);}*{box-sizing:border-box;}
        img,video{max-width:100%;}a{color:inherit}.studio-wizard-site{width:100%;margin:0;overflow-wrap:anywhere}.studio-wizard-region{min-width:0;}
        .studio-header-row{position:relative;width:100%;min-width:0;isolation:isolate}.studio-header-row-content,.studio-header-box-content{position:relative;width:100%;height:100%;min-width:0;}
        .studio-header-element{min-width:0;margin:0;overflow-wrap:anywhere}.studio-header-element>img,.studio-header-element>video{width:100%;display:block;}
        @media(max-width:760px){.studio-content-layout{display:block!important;}}
        .studio-web-navigation{min-width:0;width:100%;}.studio-web-navigation-links>a{display:inline-flex;text-decoration:none;color:var(--nav-text);background:var(--nav-fill);padding:var(--nav-padding-y) var(--nav-padding-x);border-radius:var(--nav-radius);}
        .studio-web-navigation-links>a:hover{background:var(--nav-hover)}.studio-web-navigation-links>a[aria-current=page]{background:var(--nav-active);color:#101820;}.studio-web-navigation-toggle{display:none;}
        .studio-web-navigation[data-nav-compact=true]>.studio-web-navigation-toggle{display:inline-flex;cursor:pointer;padding:.6em 1em;font:inherit;color:var(--nav-text);background:var(--nav-fill);border:1px solid currentColor;border-radius:var(--nav-radius);}
        .studio-web-navigation[data-nav-compact=true]:not([data-nav-open=true])>.studio-web-navigation-links{display:none!important;}.studio-web-navigation a:focus-visible,.studio-web-navigation-toggle:focus-visible{outline:2px solid currentColor;outline-offset:3px;}
        """;
    private const string NavigationJavaScript = """
        (()=>{'use strict';
        const canonical=value=>{try{return new URL(value,location.origin).pathname.replace(/\/+$/,'')||'/'}catch{return''}};
        const setCurrent=nav=>{for(const a of nav.querySelectorAll('a[href]')){if(canonical(a.getAttribute('href'))===canonical(location.pathname)&&new URL(a.href).origin===location.origin)a.setAttribute('aria-current','page');else a.removeAttribute('aria-current')}};
        const routeIndex=async()=>{try{const response=await fetch('/assets/studio-pages.json',{cache:'no-store',credentials:'same-origin'});if(!response.ok)return;const data=await response.json();if(data.schemaVersion!==1||!Array.isArray(data.pages)||data.pages.length>10000)return;const pages=data.pages.filter(p=>typeof p.name==='string'&&p.name.length<=300&&typeof p.route==='string'&&p.route.startsWith('/')&&!p.route.startsWith('//')&&!/[\\\x00-\x1f]/.test(p.route));
        for(const nav of document.querySelectorAll('.studio-web-navigation')){const links=nav.querySelector('.studio-web-navigation-links');if(!links)continue;for(const a of links.querySelectorAll('[data-studio-page-link]'))a.remove();const fragment=document.createDocumentFragment();const add=(label,href,rel)=>{const a=document.createElement('a');a.textContent=label;a.setAttribute('href',href);a.dataset.studioPageLink='true';if(rel)a.rel=rel;fragment.append(a)};if(nav.dataset.includeHome==='true')add(nav.dataset.homeLabel||'Home','/');if(nav.dataset.autoPages==='true')for(const p of pages.filter(p=>canonical(p.route)!=='/'))add(p.name,p.route);const position=pages.findIndex(p=>canonical(p.route)===canonical(location.pathname));if(nav.dataset.pageSteps==='true'&&position>=0){if(position>0)add('← Previous Page',pages[position-1].route,'prev');if(position+1<pages.length)add('Next Page →',pages[position+1].route,'next')}links.prepend(fragment);setCurrent(nav)}}catch{/* The generated links remain usable when the route index is unavailable. */}};
        const run=()=>{for(const nav of document.querySelectorAll('.studio-web-navigation')){setCurrent(nav);if(!nav.hasAttribute('data-nav-breakpoint')||nav.dataset.navBound)continue;nav.dataset.navBound='true';const button=nav.querySelector('.studio-web-navigation-toggle');if(!button)continue;const media=matchMedia('(max-width:'+nav.dataset.navBreakpoint+'px)');const update=()=>{nav.dataset.navCompact=String(media.matches);if(!media.matches){nav.dataset.navOpen='false';button.setAttribute('aria-expanded','false')}};media.addEventListener('change',update);button.addEventListener('click',()=>{const open=nav.dataset.navOpen!=='true';nav.dataset.navOpen=String(open);button.setAttribute('aria-expanded',String(open))});nav.addEventListener('click',event=>{if(event.target.closest('a')){nav.dataset.navOpen='false';button.setAttribute('aria-expanded','false')}});nav.addEventListener('keydown',event=>{if(event.key==='Escape'){nav.dataset.navOpen='false';button.setAttribute('aria-expanded','false');button.focus()}});update()}routeIndex()};if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',run,{once:true});else run()})();
        """;
}
