using System.IO;
using System.Net.Http;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;

public static class StudioHeaderDragVerifier
{
    public static async Task<string> RunAsync(string output,CancellationToken ct=default)
    {
        Directory.CreateDirectory(output);var checks=new List<string>();
        void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);checks.Add(message);}
        var definition=new HyperlibHeaderLayoutDefinition{Enabled=true,Blocks=[new(){Id="drag-row",Arrangement="positioned",Height=220,Elements=[
            new(){Id="drag-text",Text="Drag me",X=12.345678901234567,Y=18.125,WidthPercent=30,Height=40},
            new(){Id="drag-box",Kind="box",Arrangement="positioned",WidthMode="pixels",WidthPixels=300,Height=100,Padding=10,BorderWidth=1,Children=[
                new(){Id="drag-image",Kind="image",WidthMode="pixels",WidthPixels=100,Height=100,X=0,Y=0,MediaPath="/media/owned.svg",PositionAlignment="right"}]}]}]};
        var vm=new HyperlibWizardHeaderLayoutViewModel();vm.LoadDefinition(definition);var changed=0;vm.DefinitionChanged+=()=>changed++;
        var before=JsonSerializer.Serialize(vm.BuildDefinition(),ContentJson.Options);
        Check(vm.TryMoveElement("drag-image","drag-box",25.125,16.875,0,0),"A finished nested drag commits to its exact owning Box.");
        var image=vm.Blocks[0].Elements[1].Children[0];
        Check(changed==1&&vm.CanUndo&&ReferenceEquals(vm.SelectedElement,image),"One completed drag emits one definition update, creates one undo step, and selects its element.");
        Check(image.X==25.125&&image.Y==16.875&&image.PositionAlignment=="manual"&&image.WidthMode=="pixels"&&image.WidthPixels==100&&image.Height==100,
            "A drag updates only precise coordinates and manual alignment while preserving saved dimensions.");
        vm.Undo();Check(JsonSerializer.Serialize(vm.BuildDefinition(),ContentJson.Options)==before&&!vm.CanUndo,"One Undo restores the entire pre-drag definition without intermediate pointer positions.");
        vm.LoadDefinition(definition);changed=0;
        var text=vm.Blocks[0].Elements[0];
        Check(vm.TryMoveElement(text.Id,"drag-row",text.X,text.Y,text.X,text.Y)&&changed==0&&!vm.CanUndo,"A no-op drag succeeds without dirtying the project or creating an undo step.");
        foreach(var invalid in new[]{
            (Id:"missing",Parent:"drag-row",X:10d,Y:10d,PreviousX:(double?)null,PreviousY:(double?)null),
            (Id:"drag-text",Parent:"missing",X:10d,Y:10d,PreviousX:(double?)null,PreviousY:(double?)null),
            (Id:"drag-image",Parent:"drag-row",X:10d,Y:10d,PreviousX:(double?)null,PreviousY:(double?)null),
            (Id:"drag-text",Parent:"drag-row",X:double.NaN,Y:10d,PreviousX:(double?)null,PreviousY:(double?)null),
            (Id:"drag-text",Parent:"drag-row",X:10d,Y:double.PositiveInfinity,PreviousX:(double?)null,PreviousY:(double?)null),
            (Id:"drag-text",Parent:"drag-row",X:-.001,Y:10d,PreviousX:(double?)null,PreviousY:(double?)null),
            (Id:"drag-text",Parent:"drag-row",X:100.001,Y:10d,PreviousX:(double?)null,PreviousY:(double?)null),
            (Id:"drag-text",Parent:"drag-row",X:10d,Y:-1d,PreviousX:(double?)null,PreviousY:(double?)null),
            (Id:"drag-text",Parent:"drag-row",X:10d,Y:101d,PreviousX:(double?)null,PreviousY:(double?)null),
            (Id:"drag-text",Parent:"drag-row",X:10d,Y:10d,PreviousX:(double?)double.NaN,PreviousY:(double?)null),
            (Id:"drag-text",Parent:"drag-row",X:10d,Y:10d,PreviousX:(double?)null,PreviousY:(double?)double.NegativeInfinity),
            (Id:"drag-text",Parent:"drag-row",X:10d,Y:10d,PreviousX:(double?)1,PreviousY:(double?)null),
            (Id:"drag-text",Parent:"drag-row",X:10d,Y:10d,PreviousX:(double?)null,PreviousY:(double?)1)})
            Check(!vm.TryMoveElement(invalid.Id,invalid.Parent,invalid.X,invalid.Y,invalid.PreviousX,invalid.PreviousY)&&changed==0&&!vm.CanUndo,
                "Invalid or stale drag is rejected without mutation: "+invalid.Id+"/"+invalid.Parent+"/"+invalid.X+","+invalid.Y+"/"+invalid.PreviousX+","+invalid.PreviousY);
        Check(JsonSerializer.Serialize(vm.BuildDefinition(),ContentJson.Options)==before,"Rejected drags leave the complete saved definition unchanged.");
        foreach(var mode in new[]{"horizontal","vertical"})
        {var copy=ContentJson.Clone(definition);copy.Blocks[0].Arrangement=mode;copy.Blocks[0].Elements[1].Arrangement=mode;vm.LoadDefinition(copy);changed=0;
            Check(!vm.TryMoveElement("drag-text","drag-row",10,10)&&!vm.TryMoveElement("drag-image","drag-box",10,10)&&changed==0&&!vm.CanUndo,"Drags are rejected for ordered "+mode+" rows and Boxes.");}
        vm.LoadDefinition(definition);vm.Enabled=false;changed=0;
        Check(!vm.TryMoveElement("drag-text","drag-row",10,10)&&changed==0,"A disabled header cannot accept a stale preview drag.");
        var markup=HyperlibHeaderLayouts.RenderHtml(definition);
        Check(markup.Contains("data-header-x=\"12.345678901234567\"",StringComparison.Ordinal)&&markup.Contains("data-header-y=\"18.125\"",StringComparison.Ordinal),"Rendered drag metadata preserves original round-trip coordinates rather than CSS rounding.");
        var positioned=HyperlibHeaderLayouts.ElementStyle(definition.Blocks[0].Elements[1].Children[0],"positioned");
        Check(positioned.Contains("max-height:100%",StringComparison.Ordinal)&&!HyperlibHeaderLayouts.ElementStyle(definition.Blocks[0].Elements[1].Children[0],"horizontal").Contains("max-height:100%",StringComparison.Ordinal),
            "Positioned native media stays within the padded parent's content height while ordered media retains its explicit flow size.");
        var media=definition.Blocks[0].Elements[1].Children[0];media.PositionAlignment="manual";
        Check(HyperlibHeaderLayouts.MediaStyle(media).Contains("object-position:left top",StringComparison.Ordinal),"Contain media at manual zero coordinates aligns its visible image to the padded top-left edge.");
        media.PositionAlignment="center";Check(HyperlibHeaderLayouts.MediaStyle(media).Contains("object-position:center top",StringComparison.Ordinal),"Explicit centered media alignment retains the top edge.");
        media.PositionAlignment="right";Check(HyperlibHeaderLayouts.MediaStyle(media).Contains("object-position:right top",StringComparison.Ordinal),"Explicit right media alignment retains the top edge.");
        var result="STUDIO_HEADER_DRAG_OK|checks="+checks.Count;
        await File.WriteAllTextAsync(Path.Combine(output,"studio-header-drag-verification.json"),JsonSerializer.Serialize(new{result,checks},ContentJson.Options),ct);return result;
    }

    public static async Task<string> VerifyUpgradeAsync(string output,string oldWorkRoot,string sourceLibraryRoot,CancellationToken ct=default)
    {
        Directory.CreateDirectory(output);var original=Path.GetFullPath(Path.Combine(oldWorkRoot,"../.."));var protectedInputs=StudioGuidedUpgradeVerifier.HashTree(original);
        var library=Path.Combine(Path.GetFullPath(output),"old-free-placement-copy-"+Guid.NewGuid().ToString("N"));
        if(!library.Contains("qa",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Use isolated QA output for upgrade checks.");
        await StudioGuidedUpgradeVerifier.CopyTreeAsync(original,library,ct);var project=Path.Combine(library,Path.GetRelativePath(original,oldWorkRoot));
        var service=new HyperlibProjectPipelineService();var pipeline=(await service.ReadAsync(project,ct))!;var before=await new ContentStore(library).ReadAsync(ct);
        var oldComponent=File.ReadAllText(Path.Combine(project,"src/Pages/Blocks/StudioHeaderRowBlock.razor"));
        Require(!oldComponent.Contains("data-header-x",StringComparison.Ordinal),"Use the actual shipped pre-drag header source.");
        var definition=(await service.ReadDefinitionAsync(project,ct))!;
        pipeline=await new HyperlibWorkProjectService(sourceLibraryRoot).UpdateFoundationAsync(project,definition,pipeline.Revision,before.Revision,ct);
        var after=await new ContentStore(library).ReadAsync(ct);
        Require(before.Pages.All(pair=>after.Pages.TryGetValue(pair.Key,out var current)&&JsonSerializer.Serialize(pair.Value,ContentJson.Options)==JsonSerializer.Serialize(current,ContentJson.Options)),"An explicit drag-renderer source upgrade must preserve every saved Page, field, and citation.");
        Require(File.ReadAllText(Path.Combine(project,"src/Pages/Blocks/StudioHeaderRowBlock.razor")).Contains("data-header-x",StringComparison.Ordinal)
            &&File.ReadAllText(Path.Combine(project,"src/Pages/wwwroot/work-theme.css")).Contains("studio-header-positioned-media-v1",StringComparison.Ordinal)
            &&Directory.GetFiles(Path.Combine(library,".studio/upgrades"),"StudioHeaderRowBlock.razor",SearchOption.AllDirectories).Any(path=>File.ReadAllText(path)==oldComponent),"Upgrade must install drag metadata while backing up the exact prior source.");
        var finalInputs=StudioGuidedUpgradeVerifier.HashTree(original);
        Require(protectedInputs.Count==finalInputs.Count&&protectedInputs.All(pair=>finalInputs.GetValueOrDefault(pair.Key)==pair.Value),"The previous deployed fixture must remain unchanged.");
        var result="STUDIO_HEADER_DRAG_UPGRADE_OK|checks=4";
        await File.WriteAllTextAsync(Path.Combine(output,"studio-header-drag-upgrade-result.txt"),result+"\n"+project,ct);return result;
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
