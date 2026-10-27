using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;

public static class StudioFooterVerifier
{
    public static async Task<string> RunAsync(string output,string sourceLibraryRoot,CancellationToken ct=default)
    {
        Directory.CreateDirectory(output);var checks=new List<string>();void Check(bool value,string message){Require(value,message);checks.Add(message);}
        var generator=new HyperlibWorkProjectService(sourceLibraryRoot);var catalog=generator.GetCatalog();
        var counter=catalog.First(item=>item.GroupId=="blank"&&item.ComponentName=="CounterBlock"&&item.Kind=="element");
        var footerObject=catalog.First(item=>item.GroupId=="blank"&&item.ComponentName=="FooterBlock"&&item.Kind=="element");
        var forms=HyperlibFooterLayouts.All.Select(preset=>{var layout=HyperlibFooterLayouts.Create(preset.Id);HyperlibHeaderLayouts.Validate(layout);var json=JsonSerializer.SerializeToNode(layout,ContentJson.Options)!;RemoveIds(json);return json.ToJsonString(ContentJson.Options);}).ToArray();
        Check(forms.Length==4&&forms.Distinct(StringComparer.Ordinal).Count()==4,"Four Footer foundations contain distinct editable structures.");
        var invalid=new HyperlibHeaderElementDefinition{Kind="box",IsContentSlot=true};var invalidLayout=new HyperlibHeaderLayoutDefinition{Enabled=true,Blocks=[new(){Elements=[invalid]}]};
        Check(Rejects(()=>HyperlibHeaderLayouts.Validate(invalidLayout)),"Structural Boxes cannot silently become content slots.");
        invalid.Kind="text";invalid.SlotName=new('x',161);Check(Rejects(()=>HyperlibHeaderLayouts.Validate(invalidLayout)),"Slot names respect the supported metadata bound.");
        invalid.SlotName="Article";invalid.SlotHint="Bad\ncontrol";Check(Rejects(()=>HyperlibHeaderLayouts.Validate(invalidLayout)),"Slot guidance rejects nonprintable controls.");
        Check(HyperlibHeaderLayouts.IsSlotContentField(new(){Type="text"})&&HyperlibHeaderLayouts.IsSlotContentField(new(){Type="media"})&&!HyperlibHeaderLayouts.IsSlotContentField(new(){Type="number"}),"Content fields are separated from structural tool configuration by schema type.");
        var picture=Path.Combine(output,"footer-picture.svg");await File.WriteAllTextAsync(picture,"<svg xmlns='http://www.w3.org/2000/svg' width='100' height='60'><rect width='100' height='60' fill='#65a88e'/></svg>",ct);
        var body=new HyperlibHeaderLayoutDefinition{Enabled=true,Blocks=[new(){Id="article-slots-row",Arrangement="vertical",Height=420,Padding=12,Elements=[
            new(){Id="article-text-slot",IsContentSlot=true,SlotName="Article text",SlotHint="Write the Page text in Authoring.",Text="",FontSize=24,Height=60},
            new(){Id="article-tool-slot",Kind="template",IsContentSlot=true,SlotName="Article tool",TemplateObjectId=counter.Id,Height=170},
            new(){Id="article-image-slot",Kind="image",IsContentSlot=true,SlotName="Article picture",SlotHint="Choose a Page-specific picture in Authoring.",Height=120,WidthMode="pixels",WidthPixels=180},
            new(){Id="retained-legacy-label",Text="Existing legacy body value",FontSize=16}
        ]}]};
        var footer=HyperlibFooterLayouts.Create("page-navigation");var footerNav=HyperlibHeaderLayouts.Elements(footer).Single(element=>element.Kind=="navigation");
        footerNav.Id="footer-page-navigation";footer.Blocks[0].Id="footer-links-row";
        footer.Blocks.Add(new(){Id="footer-details-row",Arrangement="horizontal",Height=100,Padding=8,Elements=[
            new(){Id="footer-note",Text="Shared footer note",WidthPercent=70,FontSize=16},
            new(){Id="footer-image",Kind="image",MediaPath=picture,Height=60,WidthMode="pixels",WidthPixels=100,Alt="Owned footer mark"}
        ]});
        var recipe=new HyperlibPageTemplateDefinition{HeaderLayout=HyperlibHeaderLayouts.Create("simple-title"),IncludeNavigation=false,BodyLayout=body,FooterLayout=footer,
            FooterTemplateId=footerObject.Id,FooterStyle=new(){Text="Legacy Footer chrome must not duplicate"}};
        var alternate=new HyperlibNamedPageTemplateDefinition{Id="footer-other",Name="Independent foundation",Definition=ContentJson.Clone(recipe)};alternate.Definition.FooterLayout=HyperlibFooterLayouts.Create("minimal-footer");
        var project=await generator.CreateAsync(Path.Combine(output,"footer-project-"+Guid.NewGuid().ToString("N")),"site",new(){PipelineVersion=2,WorkTitle="Slots and Footer verification",SelectedToolsetIds=[footerObject.Id],LandingTemplate=ContentJson.Clone(recipe),PageTemplate=recipe,PageTemplates=[new(){Id="page-default",Name="Article Page",Definition=recipe},alternate]},ct);
        var library=Path.GetFullPath(Path.Combine(project,"../.."));var service=new HyperlibProjectPipelineService();var pipeline=(await service.ReadAsync(project,ct))!;
        var toolkit=pipeline.Toolkit.Single(item=>item.ChoiceId==counter.Id);var savedTool=HyperlibHeaderLayouts.Elements(pipeline.PageTemplate.Definition.BodyLayout).Single(element=>element.Id=="article-tool-slot");
        Check(savedTool.IsContentSlot&&savedTool.TemplateBlocks.Count==1&&savedTool.TemplateBlocks[0].Values["label"].GetString()==toolkit.SourceBlockDefinitions["element"].Fields.Single(field=>field.Id=="label").DefaultValue!.Value.GetString(),"Template slots retain underlying imported field defaults rather than destructively blanking legacy values.");
        Check(pipeline.PageTemplate.Blocks.Count(block=>block.Type==HyperlibHeaderLayouts.FooterBlockType)==2&&pipeline.PageTemplate.Blocks.Where(block=>block.Type==HyperlibHeaderLayouts.FooterBlockType).All(block=>block.Region=="footer"&&block.Values["studioRegion"].GetString()=="footer"),"Footer Blocks use their own stable row type in the actual bottom region.");
        Check(pipeline.PageTemplate.Appearance["studioFooterBuilder"].GetBoolean()&&pipeline.PageTemplate.Appearance["studioFooterText"].GetString()==""&&pipeline.PageTemplate.Appearance["studioFooterCss"].GetString()=="","Footer builder styling suppresses duplicate legacy footer chrome.");
        var ownedPicture=HyperlibHeaderLayouts.Elements(pipeline.PageTemplate.Definition.FooterLayout).Single(element=>element.Id=="footer-image").MediaPath;
        Check(ownedPicture.StartsWith("/media/",StringComparison.Ordinal)&&File.Exists(Path.Combine(library,"Media",ownedPicture[7..])),"Footer media is imported into the independently owned project.");
        var disabled=ContentJson.Clone(pipeline.PageTemplate.Definition);disabled.FooterLayout.Enabled=false;
        var restored=HyperlibProjectPipelineService.Compose(disabled,pipeline.Toolkit,TemplateObjectLibrary.ReadDefinition(project),"disabled-footer");
        Check(restored.Blocks.Any(block=>pipeline.Toolkit.Single(item=>item.ChoiceId==footerObject.Id).BlockTypeIds.Contains(block.Type))&&!restored.Blocks.Any(block=>block.Type==HyperlibHeaderLayouts.FooterBlockType),"Disabling the Footer builder recovers retained legacy footer choices.");
        var store=new ContentStore(library);var state=await store.ReadAsync(ct);var original=ContentJson.Clone(state.Pages[pipeline.LandingPageId]);
        foreach(var preset in HyperlibFooterLayouts.All){var staged=HyperlibProjectPipelineService.Compose(new(){FooterLayout=HyperlibFooterLayouts.Create(preset.Id),IncludeNavigation=false},pipeline.Toolkit,TemplateObjectLibrary.ReadDefinition(project),"footer-preset");var page=ContentJson.Clone(original);page.Revision=state.Pages[page.Id].Revision;page.Blocks=staged.Blocks;
            state=await store.ApplyAsync(new(){ExpectedRevision=state.Revision,Description="Persist Footer foundation "+preset.Title,Operations=[new(){Kind="savePage",Page=page}]},ct);Check((await store.ReadAsync(ct)).Pages[page.Id].Blocks.Count(block=>block.Type==HyperlibHeaderLayouts.FooterBlockType)==staged.Blocks.Count,preset.Title+" passes actual content validation and durable readback.");}
        original.Revision=state.Pages[original.Id].Revision;state=await store.ApplyAsync(new(){ExpectedRevision=state.Revision,Description="Restore Footer fixture",Operations=[new(){Kind="savePage",Page=original}]},ct);
        using var hosts=new HyperlibWorkPreviewHostService();var endpoint=await hosts.EnsureAsync(library,ct);using var client=new HttpClient{Timeout=TimeSpan.FromMinutes(3)};client.DefaultRequestHeaders.Add("X-HyperLib-Client","editor");
        state=await Post(client,endpoint,new(){ExpectedRevision=state.Revision,Description="Create the actual ordered Chapter outline",Operations=[
            new(){Kind="createNode",NodeType="chapter",Id="slot-chapter-one",ParentId=pipeline.WorkId,Title="First Chapter"},
            new(){Kind="createNode",NodeType="chapter",Id="slot-chapter-two",ParentId=pipeline.WorkId,Title="Second Chapter"}
        ]},ct);
        state=await Post(client,endpoint,new(){ExpectedRevision=state.Revision,Description="Create independent slot Pages in separate Chapters",Operations=[
            new(){Kind="createNode",NodeType="page",Id="slot-other-page",ParentId="slot-chapter-one",Title="Independent foundation",Appearance=new(){["studioPageTemplateId"]=ContentJson.Value(alternate.Id)}},
            new(){Kind="createNode",NodeType="page",Id="slot-first-page",ParentId="slot-chapter-one",Title="First article"},
            new(){Kind="createNode",NodeType="page",Id="slot-last-page",ParentId="slot-chapter-two",Title="Last article"}
        ]},ct);
        Check(state.Nodes.Single(node=>node.Id=="slot-first-page").Order>state.Nodes.Single(node=>node.Id=="slot-last-page").Order,"The real nested fixture has colliding sibling-local Page orders that require Chapter traversal rather than a global sort.");
        Check(new[]{"slot-first-page","slot-last-page"}.All(id=>BodyElements(state.Pages[id]).Single(element=>element.Id=="article-text-slot").Text==""&&BodyElements(state.Pages[id]).Single(element=>element.Id=="article-image-slot").MediaPath==""),"New Pages clone empty primitive slots without filling article content during Staging.");
        var alternateBefore=JsonSerializer.Serialize(state.Pages["slot-other-page"],ContentJson.Options);
        foreach(var (id,text,label) in new[]{("slot-first-page","First independent article text","First independent tool label"),("slot-last-page","Last independent article text","Last independent tool label")}){
            var page=ContentJson.Clone(state.Pages[id]);var block=page.Blocks.Single(item=>item.Type==HyperlibHeaderLayouts.BodyBlockType);var row=ReadRow(block);var textSlot=Elements(row).Single(element=>element.Id=="article-text-slot");textSlot.Text=text;
            Elements(row).Single(element=>element.Id=="article-tool-slot").TemplateBlocks[0].Values["label"]=ContentJson.Value(label);
            if(id=="slot-first-page")Elements(row).Single(element=>element.Id=="article-image-slot").MediaPath=ownedPicture;
            SaveRow(block,row);page.Sources.Add(new(){Id="source-"+id,Title="Article source",Url="https://example.com/article",RetrievedAt=DateTimeOffset.UtcNow,BlockIds=[block.Id]});
            if(id=="slot-first-page"){var details=page.Blocks.Single(item=>item.Type==HyperlibHeaderLayouts.FooterBlockType&&ReadRow(item).Id=="footer-details-row");var detailsRow=ReadRow(details);Elements(detailsRow).Single(element=>element.Id=="footer-note").Text="Independently authored footer note";SaveRow(details,detailsRow);}
            if(id=="slot-last-page"){var links=page.Blocks.Single(item=>item.Type==HyperlibHeaderLayouts.FooterBlockType&&ReadRow(item).Id=="footer-links-row");var linksRow=ReadRow(links);Elements(linksRow).Single(element=>element.Id=="footer-page-navigation").Navigation!.IncludeHome=false;SaveRow(links,linksRow);}
            state=await store.ApplyAsync(new(){ExpectedRevision=state.Revision,Description="Author independent Body slots and Footer",Operations=[new(){Kind="savePage",Page=page}]},ct);
        }
        var definition=(await service.ReadDefinitionAsync(project,ct))!;var sharedText=HyperlibHeaderLayouts.Elements(definition.PageTemplate.BodyLayout).Single(element=>element.Id=="article-text-slot");sharedText.FontSize=30;sharedText.SlotHint="Use this slot for the main article text.";
        HyperlibHeaderLayouts.Elements(definition.PageTemplate.FooterLayout).Single(element=>element.Id=="footer-note").Text="Revised shared footer note";
        pipeline=await generator.UpdateFoundationAsync(project,definition,pipeline.Revision,state.Revision,ct);state=await store.ReadAsync(ct);
        Check(BodyElements(state.Pages["slot-first-page"]).Single(element=>element.Id=="article-text-slot").Text=="First independent article text"&&BodyElements(state.Pages["slot-last-page"]).Single(element=>element.Id=="article-text-slot").Text=="Last independent article text","Revising a reusable layout preserves independently authored primitive slot text on each Page.");
        Check(BodyElements(state.Pages["slot-first-page"]).Single(element=>element.Id=="article-tool-slot").TemplateBlocks[0].Values["label"].GetString()=="First independent tool label"&&BodyElements(state.Pages["slot-last-page"]).Single(element=>element.Id=="article-tool-slot").TemplateBlocks[0].Values["label"].GetString()=="Last independent tool label","Three-way merging preserves hydrated TemplateBlocks field overrides independently on each Page.");
        Check(BodyElements(state.Pages["slot-first-page"]).Single(element=>element.Id=="article-text-slot").FontSize==30&&BodyElements(state.Pages["slot-last-page"]).Single(element=>element.Id=="article-text-slot").SlotHint==sharedText.SlotHint&&BodyElements(state.Pages["slot-last-page"]).Single(element=>element.Id=="article-image-slot").MediaPath=="","Untouched slot styling and guidance propagate without copying another Page's picture.");
        Check(FooterElements(state.Pages["slot-first-page"]).Single(element=>element.Id=="footer-note").Text=="Independently authored footer note"&&FooterElements(state.Pages["slot-last-page"]).Single(element=>element.Id=="footer-note").Text=="Revised shared footer note","Shared Footer updates retain authored text while untouched default notes follow the new foundation.");
        Check(FooterElements(state.Pages["slot-last-page"]).Single(element=>element.Id=="footer-page-navigation").Navigation!.IncludeHome==false,"A Page can retain its own previous/next-only Footer configuration during a shared foundation update.");
        Check(JsonSerializer.Serialize(state.Pages["slot-other-page"],ContentJson.Options)==alternateBefore&&state.Pages["slot-first-page"].Sources.Single().BlockIds.Contains(state.Pages["slot-first-page"].Blocks.Single(block=>block.Type==HyperlibHeaderLayouts.BodyBlockType).Id),"Slot and Footer updates isolate other named templates and preserve source citations.");
        endpoint=await hosts.EnsureAsync(library,ct);using(var runtime=JsonDocument.Parse(await client.GetStringAsync(new Uri(endpoint,"api/runtime/"+pipeline.WorkId),ct))){var prefix=runtime.RootElement.GetProperty("proxyPrefix").GetString()!;
            string Url(string id)=>new Uri(endpoint,prefix.TrimStart('/')+"page/"+id).AbsoluteUri;
            var html=await client.GetStringAsync(Url("slot-first-page"),ct);Check(html.Contains("data-studio-region=\"footer\"",StringComparison.Ordinal)&&html.Contains("First independent article text",StringComparison.Ordinal)&&!html.Contains("Legacy Footer chrome must not duplicate",StringComparison.Ordinal),"The actual generated Host renders authored slots and the new Footer without duplicate chrome.");
            var config=Path.Combine(output,"footer-browser.json");await File.WriteAllTextAsync(config,JsonSerializer.Serialize(new{firstUrl=Url(pipeline.LandingPageId),middleUrl=Url("slot-first-page"),lastUrl=Url("slot-last-page"),landingId=pipeline.LandingPageId,ownedProjectRoot=project},ContentJson.Options),ct);
            var script=Path.Combine(AppContext.BaseDirectory,"verify-studio-footer.cjs");if(!File.Exists(script))script=Path.GetFullPath(Path.Combine(Environment.CurrentDirectory,"tools/verify-studio-footer.cjs"));Require(File.Exists(script),"Copy the Footer browser verifier beside the verifier executable.");
            var run=await new BoundedProcessRunner().RunAsync(new(){ExecutablePath="C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe",Arguments=[script,config,output],WorkingDirectory=output,Timeout=TimeSpan.FromSeconds(90),MaximumStandardOutputCharacters=20000,MaximumStandardErrorCharacters=20000},ct);Check(run.ExitCode==0,"Actual Footer boundary navigation, slot rendering, and Page isolation pass browser checks. "+run.StandardOutput+run.StandardError);}
        Check(pipeline.PageTemplate.Appearance.All(item=>item.Value.GetRawText().Length<=4096),"Footer and slot definitions preserve existing Core appearance limits.");
        var result="STUDIO_FOOTER_OK|checks="+checks.Count;await File.WriteAllTextAsync(Path.Combine(output,"studio-footer-result.json"),JsonSerializer.Serialize(new{result,checks,project,bodySlotIds=new[]{"article-text-slot","article-tool-slot","article-image-slot"}},ContentJson.Options),ct);return result;
    }

    public static async Task<string> RestageSlotsAsync(string workRoot,string sourceLibraryRoot,CancellationToken ct=default)
    {
        var project=Path.GetFullPath(workRoot);var service=new HyperlibProjectPipelineService();var pipeline=await service.ReadAsync(project,ct)??throw new InvalidDataException("Select an actual independent Work project.");
        var definition=await service.ReadDefinitionAsync(project,ct)??throw new InvalidDataException("The Work has no reusable layout definition.");
        foreach(var recipe in new[]{definition.LandingTemplate,definition.PageTemplate}){
            if(recipe.BodyLayout.Blocks.FirstOrDefault() is {} row)row.Padding=row.Padding<200?row.Padding+1:199;
            foreach(var slot in HyperlibHeaderLayouts.Elements(recipe.BodyLayout).Where(element=>element.IsContentSlot))slot.SlotHint="Updated reusable layout guidance. Content stays with each Page.";
            foreach(var navigation in HyperlibHeaderLayouts.Elements(recipe.FooterLayout).Where(element=>element.Kind=="navigation"))if(navigation.Navigation is {} settings)settings.FontSize=settings.FontSize<100?settings.FontSize+1:99;
        }
        var state=await new ContentStore(Path.GetFullPath(Path.Combine(project,"../.."))).ReadAsync(ct);
        pipeline=await new HyperlibWorkProjectService(sourceLibraryRoot).UpdateFoundationAsync(project,definition,pipeline.Revision,state.Revision,ct);
        return "STUDIO_SLOTS_RESTAGED|revision="+pipeline.Revision;
    }

    public static async Task<string> VerifyUpgradeAsync(string output,string oldWorkRoot,string sourceLibraryRoot,CancellationToken ct=default)
    {
        Directory.CreateDirectory(output);var original=Path.GetFullPath(Path.Combine(oldWorkRoot,"../.."));var protectedHashes=StudioGuidedUpgradeVerifier.HashTree(original);var library=Path.Combine(Path.GetFullPath(output),"old-body-copy-"+Guid.NewGuid().ToString("N"));Require(library.Contains("qa",StringComparison.OrdinalIgnoreCase),"Use isolated QA output.");await StudioGuidedUpgradeVerifier.CopyTreeAsync(original,library,ct);
        var project=Path.Combine(library,Path.GetRelativePath(original,oldWorkRoot));var service=new HyperlibProjectPipelineService();var pipeline=(await service.ReadAsync(project,ct))!;var store=new ContentStore(library);var before=await store.ReadAsync(ct);var schema=TemplateObjectLibrary.ReadDefinition(project);
        var definition=(await service.ReadDefinitionAsync(project,ct))!;Require(!definition.LandingTemplate.FooterLayout.Enabled&&HyperlibHeaderLayouts.Elements(definition.LandingTemplate.BodyLayout).All(element=>!element.IsContentSlot),"Use a real prior Body release before Footer and content-slot metadata.");definition.LandingTemplate.FooterLayout=HyperlibFooterLayouts.Create("page-navigation");definition.PageTemplate.FooterLayout=HyperlibFooterLayouts.Create("brand-and-navigation");
        pipeline=await new HyperlibWorkProjectService(sourceLibraryRoot).UpdateFoundationAsync(project,definition,pipeline.Revision,before.Revision,ct);var after=await store.ReadAsync(ct);
        Require(before.Pages.All(pair=>after.Pages.ContainsKey(pair.Key)&&pair.Value.Blocks.Where(block=>block.Region!="footer"||schema.Blocks.Single(item=>item.Id==block.Type).Fields.Any(field=>HyperlibHeaderLayouts.IsSlotContentField(field)&&block.Values.TryGetValue(field.Id,out var value)&&value.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(value.GetString()))).All(block=>after.Pages[pair.Key].Blocks.Any(current=>current.Id==block.Id))&&JsonSerializer.Serialize(pair.Value.Sources,ContentJson.Options)==JsonSerializer.Serialize(after.Pages[pair.Key].Sources,ContentJson.Options)),"Upgrading the actual old Body project preserves authored objects, other regions, and citations.");
        foreach(var page in before.Pages.Values)foreach(var block in page.Blocks.Where(block=>block.Type==HyperlibHeaderLayouts.BodyBlockType)){var oldElements=Elements(ReadRow(block)).ToDictionary(element=>element.Id);var current=after.Pages[page.Id].Blocks.Single(item=>item.Id==block.Id);var newElements=Elements(ReadRow(current)).ToDictionary(element=>element.Id);Require(oldElements.All(pair=>newElements.TryGetValue(pair.Key,out var value)&&!value.IsContentSlot&&pair.Value.Text==value.Text&&pair.Value.MediaPath==value.MediaPath&&JsonSerializer.Serialize(pair.Value.TemplateBlocks,ContentJson.Options)==JsonSerializer.Serialize(value.TemplateBlocks,ContentJson.Options)),"Legacy Body values and real library defaults are not cleared or silently converted into slots.");}
        Require(pipeline.LandingTemplate.Blocks.Any(block=>block.Type==HyperlibHeaderLayouts.FooterBlockType)&&Directory.Exists(Path.Combine(library,".studio/upgrades")),"Explicit upgrade installs Footer Blocks and retains private source backups.");
        using var hosts=new HyperlibWorkPreviewHostService();var endpoint=await hosts.EnsureAsync(library,ct);using var client=new HttpClient{Timeout=TimeSpan.FromMinutes(3)};using(var runtime=JsonDocument.Parse(await client.GetStringAsync(new Uri(endpoint,"api/runtime/"+pipeline.WorkId),ct))){var html=await client.GetStringAsync(new Uri(endpoint,runtime.RootElement.GetProperty("proxyPrefix").GetString()!.TrimStart('/')+"page/"+pipeline.LandingPageId),ct);Require(html.Contains("data-studio-region=\"footer\"",StringComparison.Ordinal),"The actual upgraded gateway compiles and renders Footer navigation.");}
        var final=StudioGuidedUpgradeVerifier.HashTree(original);Require(final.Count==protectedHashes.Count&&protectedHashes.All(pair=>final.GetValueOrDefault(pair.Key)==pair.Value),"The prior published source fixture remains unchanged.");var result="STUDIO_FOOTER_UPGRADE_OK|checks=5";await File.WriteAllTextAsync(Path.Combine(output,"studio-footer-upgrade-result.txt"),result+"\n"+project,ct);return result;
    }
    private static HyperlibHeaderBlockDefinition ReadRow(PageBlock block)=>JsonSerializer.Deserialize<HyperlibHeaderBlockDefinition>(block.Values["layout"].GetString()!,ContentJson.Options)!;
    private static void SaveRow(PageBlock block,HyperlibHeaderBlockDefinition row)=>block.Values["layout"]=ContentJson.Value(JsonSerializer.Serialize(row,ContentJson.Options));
    private static IEnumerable<HyperlibHeaderElementDefinition> Elements(HyperlibHeaderBlockDefinition row)=>HyperlibHeaderLayouts.Elements(new(){Enabled=true,Blocks=[row]});
    private static IEnumerable<HyperlibHeaderElementDefinition> BodyElements(PageDocument page)=>page.Blocks.Where(block=>block.Type==HyperlibHeaderLayouts.BodyBlockType).SelectMany(block=>Elements(ReadRow(block)));
    private static IEnumerable<HyperlibHeaderElementDefinition> FooterElements(PageDocument page)=>page.Blocks.Where(block=>block.Type==HyperlibHeaderLayouts.FooterBlockType).SelectMany(block=>Elements(ReadRow(block)));
    private static bool Rejects(Action action){try{action();return false;}catch(InvalidDataException){return true;}}
    private static async Task<LibraryState> Post(HttpClient client,Uri endpoint,OperationBatch batch,CancellationToken ct){using var response=await client.PostAsync(new Uri(endpoint,"api/operations"),new StringContent(JsonSerializer.Serialize(batch,ContentJson.Options),Encoding.UTF8,"application/json"),ct);var raw=await response.Content.ReadAsStringAsync(ct);Require(response.IsSuccessStatusCode,raw);return JsonSerializer.Deserialize<LibraryState>(raw,ContentJson.Options)!;}
    private static void RemoveIds(JsonNode node){if(node is JsonObject obj){obj.Remove("id");foreach(var item in obj.ToArray())if(item.Value is not null)RemoveIds(item.Value);}else if(node is JsonArray arr)foreach(var item in arr)if(item is not null)RemoveIds(item);}
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
