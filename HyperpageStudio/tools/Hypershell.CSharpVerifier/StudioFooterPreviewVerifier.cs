using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;

public static class StudioFooterPreviewVerifier
{
    public static async Task<string> RunAsync(string outputDirectory,string libraryRoot)
    {
        Directory.CreateDirectory(outputDirectory);var checks=new List<string>();
        void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);checks.Add(message);}
        var image=Path.Combine(outputDirectory,"footer-picture.svg");await File.WriteAllTextAsync(image,"<svg xmlns='http://www.w3.org/2000/svg' width='120' height='80'><rect width='120' height='80' fill='#a6d5ca'/><path d='M0 80 40 15 85 80 105 45 120 80' fill='#315f67'/></svg>",new UTF8Encoding(false));
        var recipe=new HyperlibPageTemplateDefinition{IncludeNavigation=false,FooterPlacement="bottom",BackgroundColor="#142a33",
            HeaderLayout=new(){Enabled=true,Blocks=[new(){Id="shared-row",Height=70,Elements=[new(){Id="shared-text",Text="Protected Header",WidthMode="pixels",WidthPixels=220,Height=45}]}]},
            BodyLayout=new(){Enabled=true,Blocks=[new(){Id="shared-row",Height=90,Elements=[new(){Id="shared-text",Text="Protected Body",WidthMode="pixels",WidthPixels=220,Height=45}]}]},
            FooterLayout=new(){Enabled=true,Blocks=[new(){Id="shared-row",Height=250,Padding=16,BorderWidth=2,FillColor="#23474e",Elements=[
                new(){Id="shared-text",Text="Footer identity",X=4,Y=8,WidthMode="pixels",WidthPixels=190,Height=52,FillColor="#426b69",Padding=8},
                new(){Id="footer-box",Kind="box",X=42,Y=8,WidthPercent=54,Height=180,Padding=12,BorderWidth=2,FillColor="#31565d",Children=[
                    new(){Id="footer-child",Text="Nested footer",X=3,Y=5,WidthMode="pixels",WidthPixels=150,Height=45},
                    new(){Id="footer-image",Kind="image",X=3,Y=45,WidthMode="pixels",WidthPixels=105,Height=70,MediaPath=image,Alt="Footer owned image"}]}]}]}};
        var header=JsonSerializer.Serialize(recipe.HeaderLayout,ContentJson.Options);var body=JsonSerializer.Serialize(recipe.BodyLayout,ContentJson.Options);
        var definition=new HyperlibWorkProjectDefinition{PipelineVersion=2,WorkTitle="Footer preview",SelectedToolsetIds=[],LandingTemplate=ContentJson.Clone(recipe),PageTemplate=ContentJson.Clone(recipe)};
        using var staticService=new HyperlibGuidedPagePreviewService(Path.Combine(outputDirectory,"static-cache"));using var interactiveService=new HyperlibGuidedPagePreviewService(Path.Combine(outputDirectory,"interactive-cache"));
        var processes=new List<Process>();
        try{
            var pure=await staticService.PreviewAsync(libraryRoot,definition,recipe,"Static Footer builder");processes.Add(Process.GetProcessById(staticService.OwnedProcessId??throw new InvalidOperationException("Footer preview is missing.")));
            Check(!pure.Interactive,"Ordinary Footer elements use the same exact static renderer as the reader.");
            var changed=ContentJson.Clone(recipe);changed.FooterLayout.Blocks[0].Elements[0].Text="Live footer identity";changed.FooterLayout.Blocks[0].Elements[0].WidthPixels=225;
            var html=await staticService.PreviewHeaderHtmlAsync(libraryRoot,changed.FooterLayout,region:"footer");var staticPatch=HyperlibGuidedPagePreviewService.HeaderUpdateScript(html,"footer");
            Check(html.Contains("data-studio-region=\"footer\"",StringComparison.Ordinal),"Static Footer markup has its own editing region.");
            var catalog=new HyperlibWorkProjectService(libraryRoot).GetCatalog();HyperlibWorkTemplateChoice Choice(string name)=>catalog.First(item=>item.GroupId=="blank"&&item.Kind=="element"&&(item.ComponentName==name||item.ComponentName.EndsWith("."+name,StringComparison.Ordinal)));
            var counter=Choice("CounterBlock");var wave=Choice("WaveBlock");
            recipe.FooterLayout.Blocks.Add(new(){Id="footer-tool-row",Height=350,Elements=[new(){Id="footer-tool-box",Kind="box",WidthPercent=100,Height=340,Padding=12,Children=[
                new(){Id="footer-counter",Kind="template",TemplateObjectId=counter.Id,TemplateTitle=counter.Title,X=1,Y=1,WidthPercent=39,Height=190,TemplateValues=new(){["element"]=new(){["label"]=ContentJson.Value("Footer counter"),["start"]=ContentJson.Value(7)}}},
                new(){Id="footer-wave",Kind="template",TemplateObjectId=wave.Id,TemplateTitle=wave.Title,X=43,Y=1,WidthPercent=55,Height=310,TemplateValues=new(){["element"]=new(){["frequency"]=ContentJson.Value(2),["amplitude"]=ContentJson.Value(.5)}}}]}]});
            recipe.FooterLayout.Blocks.Add(new(){Id="footer-navigation-row",Arrangement="horizontal",Height=110,Padding=8,Elements=[new(){Id="footer-navigation",Kind="navigation",WidthPercent=100,Height=100,Navigation=new(){IncludeHome=true,AutoPages=false,ShowPreviousNext=true,CollapseOnSmallScreens=false}}]});
            definition.SelectedToolsetIds=[counter.Id,wave.Id];definition.LandingTemplate=ContentJson.Clone(recipe);definition.PageTemplate=ContentJson.Clone(recipe);
            var interactive=await interactiveService.PreviewAsync(libraryRoot,definition,recipe,"Footer real tools and navigation");var process=Process.GetProcessById(interactiveService.OwnedProcessId??throw new InvalidOperationException("The Footer circuit is missing."));processes.Add(process);
            Check(interactive.Interactive,"Nested Footer library tools and Navigation render through the actual interactive component circuit.");
            recipe.FooterLayout.Blocks[0].Elements[0].X=12;recipe.FooterLayout.Blocks[0].Elements[0].Y=18;recipe.FooterLayout.Blocks[0].Elements[0].WidthPixels=240;recipe.FooterLayout.Blocks[0].Elements[0].Height=80;
            var committed=await interactiveService.PreviewAsync(libraryRoot,definition,recipe,"Footer committed rectangle");
            var noSteps=ContentJson.Clone(recipe);noSteps.FooterLayout.Blocks.Last().Elements[0].Navigation!.ShowPreviousNext=false;var withoutSteps=await interactiveService.PreviewAsync(libraryRoot,definition,noSteps,"Footer without adjacent Page guides");
            Check(interactiveService.OwnedProcessId==process.Id,"Footer edits and navigation settings reuse the owned renderer.");
            Check(header==JsonSerializer.Serialize(recipe.HeaderLayout,ContentJson.Options)&&body==JsonSerializer.Serialize(recipe.BodyLayout,ContentJson.Options),"Footer changes preserve Header and Body foundations.");
            var dragScript=Path.Combine(outputDirectory,"footer-preview-drag.js");await File.WriteAllTextAsync(dragScript,HyperlibHeaderPreviewDragSource.Script,new UTF8Encoding(false));
            var config=Path.Combine(outputDirectory,"footer-preview-browser.json");await File.WriteAllTextAsync(config,JsonSerializer.Serialize(new{staticUrl=pure.Url.AbsoluteUri,interactiveUrl=interactive.Url.AbsoluteUri,committedRequestId=committed.RequestId,withoutStepsRequestId=withoutSteps.RequestId,staticPatch,dragScript},ContentJson.Options),new UTF8Encoding(false));
            var script=Path.Combine(AppContext.BaseDirectory,"verify-footer-preview.cjs");if(!File.Exists(script))throw new FileNotFoundException("Copy verify-footer-preview.cjs beside the verifier.",script);
            var result=await new BoundedProcessRunner().RunAsync(new BoundedProcessRequest{ExecutablePath="C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe",Arguments=[script,config,outputDirectory],WorkingDirectory=outputDirectory,Timeout=TimeSpan.FromSeconds(120),MaximumStandardOutputCharacters=20000,MaximumStandardErrorCharacters=20000});
            Check(result.ExitCode==0,"Footer pointer editing, nested tools, region isolation and scoped adjacent-Page guides pass browser verification. "+result.StandardOutput+result.StandardError);
        }finally{staticService.Dispose();interactiveService.Dispose();}
        foreach(var process in processes){await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));Check(process.HasExited,"The Footer verifier releases its owned preview process.");process.Dispose();}
        await File.WriteAllTextAsync(Path.Combine(outputDirectory,"footer-preview-result.json"),JsonSerializer.Serialize(new{result="STUDIO_FOOTER_PREVIEW_OK",checks},new JsonSerializerOptions{WriteIndented=true}));return "STUDIO_FOOTER_PREVIEW_OK "+checks.Count;
    }
}
