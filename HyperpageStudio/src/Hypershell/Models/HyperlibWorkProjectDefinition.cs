using System.Text.Json;
using System.Text.Json.Serialization;
using HyperLib.Core;

namespace Hypershell.Models;

/// <summary>Wizard choices for an independent Hyperlib Work and its editable Pages.</summary>
public sealed class HyperlibWorkProjectDefinition
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool DirectEditing { get; set; }
    /// <summary>Version 1 is the retained page-by-page wizard; version 2 separates toolkit, staging, and content.</summary>
    public int PipelineVersion { get; set; } = 1;
    public List<string> SelectedToolsetIds { get; set; } = [];
    public HyperlibPageTemplateDefinition LandingTemplate { get; set; } = new();
    public HyperlibPageTemplateDefinition PageTemplate { get; set; } = new();
    public List<HyperlibNamedPageTemplateDefinition> PageTemplates { get; set; } = [];
    public string DefaultPageTemplateId { get; set; } = "page-default";
    public string WorkTitle { get; set; } = "My Work";
    public string Description { get; set; } = "A place to collect, connect, and share your ideas.";
    public string TemplateId { get; set; } = "blank";
    public HyperlibWorkPageDefinition LandingPage { get; set; } = new() { Title = "Welcome" };
    public List<HyperlibWorkPageDefinition> PageDefinitions { get; set; } = [new() { Title = "Getting started" }];
}

/// <summary>Reusable structure and visual chrome. Article content belongs to authored Pages; object IDs refer to the project toolkit.</summary>
public sealed class HyperlibPageTemplateDefinition
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool DirectEditing { get; set; }
    /// <summary>Explicit gallery route: retain original content, appearance, and regions instead of staging empty slots.</summary>
    public string? CompleteTemplateObjectId { get; set; }
    public string? CompleteTemplateAppearanceId { get; set; }
    public string? BackgroundTemplateId { get; set; }
    public HyperlibPageBackgroundDefinition BackgroundSurface { get; set; } = new();
    public HyperlibHeaderLayoutDefinition HeaderLayout { get; set; } = new();
    public HyperlibHeaderLayoutDefinition BodyLayout { get; set; } = new();
    public HyperlibHeaderLayoutDefinition FooterLayout { get; set; } = new();
    public HyperlibHeaderLayoutDefinition NavigationLayout { get; set; } = new();
    public bool NavigationSticky { get; set; }
    public HyperlibTemplateRegionStyle HeaderStyle { get; set; } = new();
    public HyperlibTemplateRegionStyle BodyStyle { get; set; } = new();
    public HyperlibTemplateRegionStyle NavigationStyle { get; set; } = new();
    public HyperlibTemplateRegionStyle FooterStyle { get; set; } = new();
    public HyperlibTemplateRegionStyle ExtrasStyle { get; set; } = new();
    public string? LayoutTemplateId { get; set; }
    public string? BodyLayoutTemplateId { get; set; }
    public string? HeaderTemplateId { get; set; }
    public string? FooterTemplateId { get; set; }
    public string? NavigationTemplateId { get; set; }
    public string? BodyTemplateId { get; set; }
    public string BackgroundColor { get; set; } = "#101820";
    public string AccentColor { get; set; } = "#65a88e";
    public string ContentAlignment { get; set; } = "left";
    public string ContentWidth { get; set; } = "comfortable";
    public string HeaderPlacement { get; set; } = "top";
    public string FooterPlacement { get; set; } = "bottom";
    public string NavigationPlacement { get; set; } = "top";
    public bool IncludeNavigation { get; set; } = true;
    public List<HyperlibTemplatePlacementDefinition> RepeatingObjects { get; set; } = [];
}

/// <summary>The whole Page's background and outer border, independent of its content regions.</summary>
public sealed class HyperlibPageBackgroundDefinition
{
    public string DesignId { get; set; } = "solid";
    public string ImagePath { get; set; } = "";
    public string ImageFit { get; set; } = "cover";
    public string BorderColor { get; set; } = "#65a88e";
    public double BorderWidth { get; set; }
    public string BorderStyle { get; set; } = "solid";
    public double BorderRadius { get; set; }
}

public sealed class HyperlibHeaderLayoutDefinition
{
    public bool Enabled { get; set; }
    public List<HyperlibHeaderBlockDefinition> Blocks { get; set; } = [];
}

public sealed class HyperlibHeaderBlockDefinition
{
    public string Id { get; set; } = "hrow-" + Guid.NewGuid().ToString("N");
    public string Arrangement { get; set; } = "positioned";
    public double Height { get; set; } = 160;
    public string FillColor { get; set; } = "";
    public string BorderColor { get; set; } = "#65a88e";
    public double BorderWidth { get; set; }
    public string BorderStyle { get; set; } = "solid";
    public double BorderRadius { get; set; }
    public double Padding { get; set; }
    public List<HyperlibHeaderElementDefinition> Elements { get; set; } = [];
}

public sealed class HyperlibHeaderElementDefinition
{
    public string Id { get; set; } = "hel-" + Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "text";
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsContentSlot { get; set; }
    public string SlotName { get; set; } = "";
    public string SlotHint { get; set; } = "";
    public string Arrangement { get; set; } = "positioned";
    public string WidthMode { get; set; } = "percent";
    public double WidthPixels { get; set; } = 128;
    public double WidthPercent { get; set; } = 100;
    public double Height { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public string Alignment { get; set; } = "left";
    public string PositionAlignment { get; set; } = "manual";
    public string FontFamily { get; set; } = "system-ui";
    public double FontSize { get; set; } = 24;
    public int FontWeight { get; set; } = 400;
    public string Text { get; set; } = "";
    public string TextColor { get; set; } = "#f2f5f4";
    public string FillColor { get; set; } = "";
    public string BorderColor { get; set; } = "#65a88e";
    public double BorderWidth { get; set; }
    public string BorderStyle { get; set; } = "solid";
    public double BorderRadius { get; set; }
    public double Padding { get; set; }
    public string MediaPath { get; set; } = "";
    public string Alt { get; set; } = "";
    public string MediaFit { get; set; } = "contain";
    public HyperlibNavigationDefinition? Navigation { get; set; }
    public string TemplateObjectId { get; set; } = "";
    public string TemplateTitle { get; set; } = "";
    /// <summary>Source block IDs (or "element") map to independently editable field overrides.</summary>
    public Dictionary<string, Dictionary<string, JsonElement>> TemplateValues { get; set; } = [];
    /// <summary>Hydrated portable components; rebuilt from the project's owned toolkit when staging.</summary>
    public List<PageBlock> TemplateBlocks { get; set; } = [];
    public Dictionary<string, JsonElement> TemplateAppearance { get; set; } = [];
    public List<HyperlibHeaderElementDefinition> Children { get; set; } = [];
}

public sealed class HyperlibNavigationDefinition
{
    public string FontFamily { get; set; } = "system-ui";
    public bool AutoPages { get; set; } = true;
    public bool IncludeHome { get; set; } = true;
    public string HomeLabel { get; set; } = "Home";
    public bool ShowPreviousNext { get; set; }
    public string Orientation { get; set; } = "horizontal";
    public string Alignment { get; set; } = "left";
    public double Gap { get; set; } = 12;
    public double LinkPaddingX { get; set; } = 14;
    public double LinkPaddingY { get; set; } = 10;
    public double Radius { get; set; } = 6;
    public double FontSize { get; set; } = 16;
    public string TextColor { get; set; } = "#dce9e2";
    public string BackgroundColor { get; set; } = "";
    public string HoverColor { get; set; } = "#26483e";
    public string ActiveColor { get; set; } = "#65a88e";
    public bool CollapseOnSmallScreens { get; set; } = true;
    public double CollapseBreakpoint { get; set; } = 720;
    public string MenuLabel { get; set; } = "Menu";
    public List<HyperlibNavigationLinkDefinition> CustomLinks { get; set; } = [];
}

public sealed class HyperlibNavigationLinkDefinition
{
    public string Id { get; set; } = "navlink-" + Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "New link";
    public string Target { get; set; } = "#";
    public bool NewTab { get; set; }
}

public sealed class HyperlibNamedPageTemplateDefinition
{
    public string Id { get; set; } = "page-default";
    public string Name { get; set; } = "Content Page";
    public HyperlibPageTemplateDefinition Definition { get; set; } = new();
}

/// <summary>Reusable visual chrome. Article text and media remain independently authored Page content.</summary>
public sealed class HyperlibTemplateRegionStyle
{
    public string BackgroundColor { get; set; } = "";
    public string TextColor { get; set; } = "";
    public string BorderColor { get; set; } = "";
    public double BorderWidth { get; set; }
    public double BorderRadius { get; set; }
    public double Padding { get; set; } = 16;
    public string FontFamily { get; set; } = "inherit";
    public double FontSize { get; set; } = 18;
    public string Alignment { get; set; } = "left";
    public string LogoPlacement { get; set; } = "left";
    public string LogoPath { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class HyperlibTemplatePlacementDefinition
{
    public string Id { get; set; } = "";
    public string TemplateObjectId { get; set; } = "";
    public string Region { get; set; } = "main";
    public int Order { get; set; }
}

public sealed class HyperlibWorkPageDefinition
{
    public string Title { get; set; } = "New Page";
    public string Description { get; set; } = "";
    public string? LayoutChoiceId { get; set; }
    public string? HeaderChoiceId { get; set; }
    public string? BodyChoiceId { get; set; }
    public string? ImageChoiceId { get; set; }
    public string? ImagePath { get; set; }
    public string ImageAlt { get; set; } = "";
    public List<HyperlibWorkImageDefinition> Images { get; set; } = [];
    public string? Background { get; set; }
    public string? Accent { get; set; }
    public string? BackgroundImagePath { get; set; }
    public bool IncludeNavigation { get; set; } = true;
    public List<string> SpecialChoiceIds { get; set; } = [];
}

public sealed class HyperlibWorkImageDefinition
{
    public string Path { get; set; } = "";
    public string Alt { get; set; } = "";
    public string Caption { get; set; } = "";
}

public sealed record HyperlibWorkStarterChoice(string Id, string Title, string Description,
    string Category, string Background, string Accent);

public sealed record HyperlibWorkTemplateChoice(string Id, string Title, string Description,
    string Category, string Kind, string GroupId, string ObjectId, string GroupTitle, string ComponentName);
