using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HyperLib.Core;
using Hypershell.Models;

namespace Hypershell.Services;

public static class HyperlibNavigationLayouts
{
    public const string LinksBlockType="studio-navigation-links";
    public const string LayoutBlockType="studio-navigation-layout";
    public static IReadOnlyList<HyperlibHeaderPreset> All {get;}=Array.AsReadOnly(new HyperlibHeaderPreset[]{
        new("simple-links","Simple links","A clean row of links that follows the Pages in your Work."),
        new("brand-and-links","Brand and links","An editable site name beside a flexible links area."),
        new("centered-tabs","Centered tabs","Centered navigation with generous spacing and rounded active tabs."),
        new("two-level","Two-level bar","A slim identity row above the primary navigation."),
        new("sidebar","Sidebar","A vertical navigation Box with a title and stacked links."),
        new("outlined-pills","Outlined pills","A padded navigation panel with distinct rounded links.")});

    public static HyperlibHeaderLayoutDefinition Create(string id)
    {
        if(!All.Any(item=>item.Id==id))throw new InvalidDataException("Choose an available Navigation layout.");
        var links=new HyperlibHeaderElementDefinition{Kind="navigation",FontSize=16,WidthPercent=100,Navigation=new()};
        var settings=links.Navigation!;
        var row=new HyperlibHeaderBlockDefinition{Height=64,Arrangement="horizontal",Padding=8,Elements=[links]};
        var result=new HyperlibHeaderLayoutDefinition{Enabled=true,Blocks=[row]};
        switch(id)
        {
            case "brand-and-links": row.Height=76;row.Elements.Insert(0,new(){Text="Your site",FontSize=22,FontWeight=600,WidthPercent=28});links.WidthPercent=72;settings.Alignment="right";break;
            case "centered-tabs": settings.Alignment="center";settings.Gap=18;settings.Radius=12;row.Padding=12;break;
            case "two-level": result.Blocks.Insert(0,new(){Height=44,Arrangement="horizontal",FillColor="#173d35",Padding=10,Elements=[new(){Text="Your site · Explore this Work",FontSize=16,FontWeight=600}]});break;
            case "sidebar": row.Height=280;row.Elements=[new(){Kind="box",Arrangement="vertical",Height=0,FillColor="#17312e",Padding=14,BorderRadius=12,Children=[new(){Text="Explore",FontSize=20,Height=38},links]}];settings.Orientation="vertical";settings.CollapseOnSmallScreens=false;break;
            case "outlined-pills": row.Height=86;row.FillColor="#102d28";row.BorderWidth=1;row.BorderRadius=14;row.Padding=12;settings.BackgroundColor="#21463d";settings.HoverColor="#326352";settings.Radius=24;settings.Gap=10;break;
        }
        return result;
    }

    public static void EnsureBlockDefinitions(TemplateDefinition template)
    {
        HyperlibHeaderLayouts.EnsureBlockDefinition(template);
        var header=template.Blocks.Single(block=>block.Id==HyperlibHeaderLayouts.BlockType);
        if(!header.Fields.Any(field=>field.Id=="studioRegion"))header.Fields.Add(new(){Id="studioRegion",Title="Builder region",Type="string",DefaultValue=ContentJson.Value("header")});
        Ensure(LinksBlockType,"Navigation links","StudioNavigationLinksBlock",[new(){Id="settings",Title="Navigation links and styles",Type="text",DefaultValue=ContentJson.Value("{}")}]);
        Ensure(LayoutBlockType,"Navigation layout","StudioNavigationLayoutBlock",[new(){Id="layout",Title="Navigation Blocks",Type="text",DefaultValue=ContentJson.Value("{}")},new(){Id="sticky",Title="Sticky bar",Type="boolean",DefaultValue=ContentJson.Value(false)}]);
        void Ensure(string id,string title,string component,List<FieldDefinition> fields)
        {if(template.Blocks.SingleOrDefault(block=>block.Id==id) is {} existing){if(existing.Component!=component)throw new InvalidDataException("This project uses a reserved Studio Navigation block ID for custom source.");return;}
            template.Blocks.Add(new(){Id=id,Title=title,Component=component,Category="Navigation",Fields=fields});}
    }

    public static void Validate(HyperlibNavigationDefinition? value)
    {
        if(value is null||value.CustomLinks is null)throw new InvalidDataException("Navigation settings are missing.");
        if(value.Orientation is not("horizontal" or "vertical")||value.Alignment is not("left" or "center" or "right"))throw new InvalidDataException("Choose a supported navigation direction and alignment.");
        if(!Regex.IsMatch(value.FontFamily??"","^[a-zA-Z0-9 ,_-]{1,100}$",RegexOptions.CultureInvariant))throw new InvalidDataException("Choose a standard Navigation font family.");
        Number(value.Gap,0,120);Number(value.LinkPaddingX,0,100);Number(value.LinkPaddingY,0,100);Number(value.Radius,0,100);Number(value.FontSize,8,100);Number(value.CollapseBreakpoint,320,1600);
        foreach(var color in new[]{value.TextColor,value.HoverColor,value.ActiveColor})Color(color,false);Color(value.BackgroundColor,true);
        Text(value.HomeLabel,100);Text(value.MenuLabel,100);
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var link in value.CustomLinks)
        {if(link is null)throw new InvalidDataException("A custom navigation link is missing.");TemplateValidator.ValidateId(link.Id,"navigation link");if(!ids.Add(link.Id))throw new InvalidDataException("Navigation links need unique IDs.");Text(link.Label,200);if(!IsSafeTarget(link.Target))throw new InvalidDataException("Give each link a Page ID (page:ID), a real #anchor, a rooted path, or an HTTP(S), mailto, or tel address.");}
    }
    private static void Text(string value,int maximum){if(string.IsNullOrWhiteSpace(value)||value.Length>maximum||value.Any(char.IsControl))throw new InvalidDataException("Name the navigation controls and links with printable text.");}
    private static void Number(double value,double minimum,double maximum){if(!double.IsFinite(value)||value<minimum||value>maximum)throw new InvalidDataException("Choose supported Navigation spacing and sizes.");}
    private static void Color(string value,bool empty){if(!(empty&&value=="")&&!Regex.IsMatch(value??"","^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$",RegexOptions.CultureInvariant))throw new InvalidDataException("Choose a hexadecimal Navigation color.");}
    public static bool IsSafeTarget(string? target)
    {
        if(string.IsNullOrWhiteSpace(target)||target.Length>2048||target.Any(char.IsControl)||target.Contains('\\'))return false;
        if(target.StartsWith("page:",StringComparison.Ordinal)){try{TemplateValidator.ValidateId(target[5..],"Page");return !Regex.IsMatch(target[5..],"^(?:con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\\..*)?$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);}catch(ContentValidationException){return false;}}
        if(target.StartsWith('#'))return target.Length>1&&!target.Any(char.IsWhiteSpace);
        if(target.StartsWith('/')){string decoded;try{decoded=Uri.UnescapeDataString(target);}catch(UriFormatException){return false;}return !decoded.StartsWith("//",StringComparison.Ordinal)&&!decoded.Contains('\\')&&!decoded.Any(char.IsControl)&&!decoded.Split('?', '#')[0].Split('/').Any(segment=>segment is "." or "..");}
        return Uri.TryCreate(target,UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https" or "mailto" or "tel"&&(uri.Scheme is "mailto" or "tel"?target[(target.IndexOf(':')+1)..].Split('?')[0].Trim().Length>0:string.IsNullOrEmpty(uri.UserInfo));
    }
    public static string ResolveTarget(string target,string? landingId=null)
    {if(!IsSafeTarget(target))throw new InvalidDataException("This navigation target is unsafe.");return target.StartsWith("page:",StringComparison.Ordinal)?"page/"+Uri.EscapeDataString(target[5..])
        :target=="/"&&!string.IsNullOrEmpty(landingId)?"page/"+Uri.EscapeDataString(landingId):target.StartsWith("/page/",StringComparison.Ordinal)?target[1..]:target;}
    public static string LinkStyle(HyperlibNavigationDefinition value)
    {Validate(value);return "--nav-gap:"+N(value.Gap)+"px;--nav-px:"+N(value.LinkPaddingX)+"px;--nav-py:"+N(value.LinkPaddingY)+"px;--nav-radius:"+N(value.Radius)+"px;--nav-font:"+N(value.FontSize)
        +"px;--nav-family:"+value.FontFamily+";--nav-text:"+value.TextColor+";--nav-fill:"+(value.BackgroundColor.Length>0?value.BackgroundColor:"transparent")+";--nav-hover:"+value.HoverColor+";--nav-hover-text:"+Contrast(value.HoverColor)+";--nav-active:"+value.ActiveColor+";--nav-active-text:"+Contrast(value.ActiveColor)+";--nav-justify:"+(value.Alignment=="center"?"center":value.Alignment=="right"?"flex-end":"flex-start")+";";}
    private static string Contrast(string color){var hex=color.Length==4?string.Concat(color.Skip(1).SelectMany(character=>new[]{character,character})):color[1..7];return Convert.ToInt32(hex[..2],16)*.299+Convert.ToInt32(hex[2..4],16)*.587+Convert.ToInt32(hex[4..6],16)*.114>145?"#10241c":"#ffffff";}
    public static string Key(string value)=>"nav-"+Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];
    public static string ResponsiveCss(string key,HyperlibNavigationDefinition value)
    {Validate(value);if(!Regex.IsMatch(key,"^nav-[a-f0-9]{12}$",RegexOptions.CultureInvariant))throw new InvalidDataException("Invalid Navigation instance.");return value.CollapseOnSmallScreens?"@media(max-width:"+N(value.CollapseBreakpoint)+"px){."+key+" > .studio-nav-menu{display:inline-flex}."+key+" > .studio-nav-items:not([data-open=true]){display:none}."+key+" > .studio-nav-items{flex-direction:column;align-items:stretch;justify-content:flex-start;flex-wrap:nowrap;max-height:calc(100dvh - 96px);overflow-y:auto;overscroll-behavior:contain}."+key+" > .studio-nav-items>li{flex-shrink:0}}":"";}
    private static string N(double value)=>value.ToString("0.###",CultureInfo.InvariantCulture);

    public static string MergeSerializedLayout(string current,string previous,string next)
    {
        var live=JsonSerializer.Deserialize<HyperlibHeaderLayoutDefinition>(current,ContentJson.Options)!;var old=JsonSerializer.Deserialize<HyperlibHeaderLayoutDefinition>(previous,ContentJson.Options)!;
        var target=JsonSerializer.Deserialize<HyperlibHeaderLayoutDefinition>(next,ContentJson.Options)!;
        if(live.Enabled!=old.Enabled)target.Enabled=live.Enabled;
        var output=new List<HyperlibHeaderBlockDefinition>();
        foreach(var row in target.Blocks){var currentRow=live.Blocks.SingleOrDefault(item=>item.Id==row.Id);var prior=old.Blocks.SingleOrDefault(item=>item.Id==row.Id);
            output.Add(currentRow is not null&&prior is not null?JsonSerializer.Deserialize<HyperlibHeaderBlockDefinition>(HyperlibHeaderLayouts.MergeSerializedBlock(JsonSerializer.Serialize(currentRow,ContentJson.Options),JsonSerializer.Serialize(prior,ContentJson.Options),JsonSerializer.Serialize(row,ContentJson.Options)),ContentJson.Options)!:row);}
        foreach(var row in live.Blocks.Where(row=>!target.Blocks.Any(item=>item.Id==row.Id)))if(!old.Blocks.Any(item=>item.Id==row.Id&&JsonNode.DeepEquals(JsonSerializer.SerializeToNode(item,ContentJson.Options),JsonSerializer.SerializeToNode(row,ContentJson.Options))))output.Add(row);
        target.Blocks=output;HyperlibHeaderLayouts.Validate(target);return JsonSerializer.Serialize(target,ContentJson.Options);
    }
    public const string Css="""
        /* studio-navigation-layouts-v1 */
        .studio-staged-layout:has(.studio-navigation-layout){grid-template-areas:"header" "navigation" "sidebar" "main" "footer";}
        .studio-staged-layout [data-region="navigation"]{grid-area:navigation;}
        .studio-staged-layout [data-region="navigation"]:has(.studio-navigation-layout[data-sticky=true]){position:sticky;top:0;z-index:30;align-self:start;}
        .studio-staged-layout[data-studio-navigation="left"]:has(.studio-navigation-layout){grid-template-columns:minmax(170px,240px) minmax(0,1fr);grid-template-areas:"header header" "navigation main" "sidebar sidebar" "footer footer";}
        .studio-staged-layout[data-studio-navigation="right"]:has(.studio-navigation-layout){grid-template-columns:minmax(0,1fr) minmax(170px,240px);grid-template-areas:"header header" "main navigation" "sidebar sidebar" "footer footer";}
        .studio-staged-layout[data-studio-navigation="bottom"]:has(.studio-navigation-layout){grid-template-areas:"header" "sidebar" "main" "navigation" "footer";}
        @media(max-width:760px){.studio-staged-layout[data-studio-navigation]:has(.studio-navigation-layout){grid-template-columns:minmax(0,1fr);grid-template-areas:"header" "navigation" "sidebar" "main" "footer";}}
        .hl-block:has(> .studio-navigation-layout){grid-column:1/-1;min-width:0;}
        .hl-block:has(> .studio-navigation-layout[data-sticky=true]){position:sticky;top:0;z-index:30;align-self:start;}
        .studio-navigation-layout{width:100%;min-width:0;isolation:isolate;background:var(--hl-paper);}
        .studio-navigation-links{width:100%;min-width:0;color:var(--nav-text);font:500 var(--nav-font)/1.4 var(--nav-family,system-ui,sans-serif);}
        .studio-nav-items{display:flex;flex-wrap:wrap;gap:var(--nav-gap);justify-content:var(--nav-justify);list-style:none;padding:0;margin:0;}
        .studio-navigation-links[data-direction=vertical]>.studio-nav-items{flex-direction:column;align-items:stretch;}
        .studio-nav-items li{min-width:0;margin:0;}
        .studio-nav-items a,.studio-nav-menu{display:block;padding:var(--nav-py) var(--nav-px);border:0;border-radius:var(--nav-radius);color:var(--nav-text);background:var(--nav-fill);font:inherit;text-decoration:none;overflow-wrap:anywhere;}
        .studio-nav-items a:hover,.studio-nav-items a:focus-visible,.studio-nav-menu:hover{background:var(--nav-hover);color:var(--nav-hover-text);}
        .studio-nav-items a[aria-current=page]{background:var(--nav-active);color:var(--nav-active-text);box-shadow:inset 0 -2px 0 currentColor;}
        .studio-nav-menu{display:none;cursor:pointer;}
        .studio-nav-steps{display:flex;justify-content:space-between;gap:var(--nav-gap);margin-top:var(--nav-gap);}
        .studio-nav-empty{margin:0;font:inherit;opacity:.65;}
        """;
}
