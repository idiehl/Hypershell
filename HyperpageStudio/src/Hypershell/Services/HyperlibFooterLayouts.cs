using System.IO;
using HyperLib.Core;
using Hypershell.Models;

namespace Hypershell.Services;

public static class HyperlibFooterLayouts
{
    public const string BlockType = HyperlibHeaderLayouts.FooterBlockType;
    public static IReadOnlyList<HyperlibHeaderPreset> All { get; } = Array.AsReadOnly(new HyperlibHeaderPreset[] {
        new("page-navigation", "Page navigation", "Work home and previous/next Page links."),
        new("minimal-footer", "Minimal footer", "A simple repeating footer line."),
        new("brand-and-navigation", "Brand and navigation", "Site identity beside Page navigation."),
        new("three-columns", "Three columns", "A site note, useful links, and Page navigation.") });
    public static void EnsureBlockDefinition(TemplateDefinition template)
        => HyperlibHeaderLayouts.EnsureBlockDefinition(template, BlockType, "Footer");
    public static HyperlibHeaderLayoutDefinition Create(string id)
    {
        if(!All.Any(item=>item.Id==id))throw new InvalidDataException("Choose an available Footer layout.");
        var nav=new HyperlibHeaderElementDefinition { Kind="navigation", WidthPercent=100, FontSize=16,
            Navigation=new(){AutoPages=false,IncludeHome=true,HomeLabel="Work home",ShowPreviousNext=true,CollapseOnSmallScreens=false} };
        var row=new HyperlibHeaderBlockDefinition { Height=160, Arrangement="horizontal", Padding=16, BorderWidth=1, Elements=[nav] };
        var result=new HyperlibHeaderLayoutDefinition { Enabled=true, Blocks=[row] };
        switch(id)
        {
            case "minimal-footer":row.Height=70;row.Elements=[new(){Text="© Your site",FontSize=14,Alignment="center"}];break;
            case "brand-and-navigation":row.Height=170;nav.WidthPercent=70;row.Elements.Insert(0,new(){Text="Your site",FontSize=22,FontWeight=600,WidthPercent=30});break;
            case "three-columns":row.Height=240;row.Elements=[Box(30,new(){Text="Your site",FontSize=22,FontWeight=600},new(){Text="A short site note",FontSize=16}),Box(30,new(){Text="Explore",FontSize=18,FontWeight=600},new(){Kind="navigation",FontSize=16,Navigation=new(){AutoPages=true,IncludeHome=true,CollapseOnSmallScreens=true,Orientation="vertical"}}),Box(40,nav)];break;
        }
        return result;
    }
    private static HyperlibHeaderElementDefinition Box(double width,params HyperlibHeaderElementDefinition[] elements)
        =>new(){Kind="box",Arrangement="vertical",WidthPercent=width,Height=200,Padding=12,Children=elements.ToList()};
}
