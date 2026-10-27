using System.Text.Json;
using HyperLib.Core;
using Hypershell;
using Hypershell.Models;
using Hypershell.ViewModels;

public static class StudioHeaderResizeVerifier
{
 public static async Task<string> RunAsync(string output,CancellationToken ct=default)
 {
  Directory.CreateDirectory(output);var checks=new List<string>();void Check(bool yes,string message){if(!yes)throw new InvalidOperationException(message);checks.Add(message);}
  var definition=new HyperlibHeaderLayoutDefinition{Enabled=true,Blocks=[new(){Id="resize-row",Height=200,Elements=[new(){Id="resize-text",X=10,Y=10,WidthPercent=50,Height=40,Text="Resize"}]}]};
  var vm=new HyperlibWizardHeaderLayoutViewModel();vm.LoadDefinition(definition);var element=vm.Blocks[0].Elements[0];var original=element.BuildDefinition();var changes=0;vm.DefinitionChanged+=()=>changes++;
  Check(vm.TryResizeElement(element.Id,"resize-row","positioned","se",10,10,240,70,400,200,original),"One finished resize commits against its exact parent and typed baseline.");
  Check(changes==1&&vm.CanUndo&&element.WidthMode=="percent"&&element.WidthPercent==60&&element.Height==70&&element.PositionAlignment=="manual","Percentage width remains responsive and the entire resize emits one change and one undo entry.");
  vm.Undo();Check(JsonSerializer.Serialize(vm.BuildDefinition(),ContentJson.Options)==JsonSerializer.Serialize(definition,ContentJson.Options),"One Undo restores width, height, units and coordinates together.");
  vm.LoadDefinition(definition);element=vm.Blocks[0].Elements[0];original=element.BuildDefinition();changes=0;
  Check(!vm.TryResizeElement(element.Id,"wrong","positioned","se",10,10,240,70,400,200,original)
   &&!vm.TryResizeElement(element.Id,"resize-row","horizontal","se",10,10,240,70,400,200,original)
   &&!vm.TryResizeElement(element.Id,"resize-row","positioned","se",10,10,double.NaN,70,400,200,original)
   &&!vm.TryResizeElement(element.Id,"resize-row","positioned","se",90,90,240,70,400,200,original)&&changes==0&&!vm.CanUndo,"Wrong-parent, stale-mode, nonfinite and out-of-bounds rectangles cannot change the model.");
  original.WidthPercent=49;Check(!vm.TryResizeElement(element.Id,"resize-row","positioned","e",10,10,240,40,400,200,original),"A stale native width baseline rejects the resize.");
  original=element.BuildDefinition();Check(!vm.TryResizeElement(element.Id,"resize-row","positioned","e",10,10,16,40,4000,200,original)
   &&vm.TryResizeElement(element.Id,"resize-row","positioned","e",10,10,40,40,4000,200,original)&&element.WidthPercent==1,
   "A wide parent enforces the same one-percent minimum in pointer geometry and committed responsive units.");
  var pixel=ContentJson.Clone(definition);pixel.Blocks[0].Elements[0].WidthMode="pixels";pixel.Blocks[0].Elements[0].WidthPixels=150;pixel.Blocks[0].Elements[0].Height=0;
  vm.LoadDefinition(pixel);element=vm.Blocks[0].Elements[0];
  Check(vm.TryResizeElement(element.Id,"resize-row","positioned","e",10,10,220,20,400,200,element.BuildDefinition())&&element.WidthMode=="pixels"&&element.WidthPixels==220&&element.Height==0,"A width-only resize retains pixel units and natural auto height.");
  Check(vm.TryResizeElement(element.Id,"resize-row","positioned","s",10,10,220,65,400,200,element.BuildDefinition())&&element.Height==65,"Vertically resizing natural content produces an explicit pixel height.");
  var flow=ContentJson.Clone(definition);flow.Blocks[0].Arrangement="horizontal";flow.Blocks[0].Elements[0].PositionAlignment="right";
  vm.LoadDefinition(flow);element=vm.Blocks[0].Elements[0];
  Check(vm.TryResizeElement(element.Id,"resize-row","horizontal","se",0,0,240,400,400,40,element.BuildDefinition())&&element.X==10&&element.Y==10&&element.PositionAlignment=="right"&&element.Height==400,"A flow resize changes size and permits row growth while retaining ordered placement and alignment.");
  var padded=ContentJson.Clone(definition);padded.Blocks[0].Elements[0].Kind="box";padded.Blocks[0].Elements[0].Padding=18;padded.Blocks[0].Elements[0].BorderWidth=2;
  vm.LoadDefinition(padded);element=vm.Blocks[0].Elements[0];
  Check(!vm.TryResizeElement(element.Id,"resize-row","positioned","se",10,10,16,16,400,200,element.BuildDefinition())
   &&vm.TryResizeElement(element.Id,"resize-row","positioned","se",10,10,40,40,400,200,element.BuildDefinition()),"The atomic model rejects a rectangle below padding and border, while accepting its physical minimum.");
  Check(vm.TryResizeElement(element.Id,"resize-row","positioned","se",10,10,70,60,400,200,element.BuildDefinition()),"A committed physical-minimum Box accepts its next resize using the updated baseline.");
  var document=new Uri("http://127.0.0.1:53210/interactive/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
  HyperlibHeaderDragCapability Capability(string region="header"){var cap=new HyperlibHeaderDragCapability("resize-token",2,"resize-document","page-template",document,ContentJson.Clone(definition),region);cap.TryReceive(document.AbsoluteUri,document.AbsoluteUri,JsonSerializer.Serialize(new{type="studio-header-drag-ready",token=cap.Token,generation=2,documentId=cap.DocumentId,region}),2,out _);return cap;}
  Dictionary<string,object?> Payload()=>new(){["type"]="studio-header-resize",["token"]="resize-token",["generation"]=2,["documentId"]="resize-document",["region"]="header",["elementId"]="resize-text",["parentId"]="resize-row",["handle"]="se",["x"]=10,["y"]=10,["width"]=240,["height"]=70,["parentWidth"]=400,["parentHeight"]=200,["sequence"]=1,
   ["previous"]=new Dictionary<string,object?>{["x"]=10,["y"]=10,["widthMode"]="percent",["widthPercent"]=50,["widthPixels"]=128,["height"]=40,["positionAlignment"]="manual",["renderedWidth"]=200,["renderedHeight"]=40}};
  bool Receive(HyperlibHeaderDragCapability cap,Dictionary<string,object?> payload,string? source=null,long version=2)=>cap.TryReceiveResize(source??document.AbsoluteUri,document.AbsoluteUri,JsonSerializer.Serialize(payload),version,out _);
  var current=Capability();var payload=Payload();
  Check(Receive(current,payload)&&!Receive(current,payload),"The real bridge accepts one current resize and rejects its replay.");
  foreach(var pair in new Dictionary<string,object?>{["token"]="old",["generation"]=1,["documentId"]="other",["region"]="navigation",["parentId"]="other",["handle"]="diagonal",["width"]=500,["parentWidth"]=800})
  {var invalid=Payload();invalid[pair.Key]=pair.Value;Check(!Receive(Capability(),invalid),"The resize capability rejects forged or stale "+pair.Key+".");}
  foreach(var pair in new Dictionary<string,object?>{["widthMode"]="pixels",["widthPercent"]=49,["height"]=41,["renderedWidth"]=100})
  {var invalid=Payload();((Dictionary<string,object?>)invalid["previous"]!)[pair.Key]=pair.Value;Check(!Receive(Capability(),invalid),"The resize capability rejects a mismatched previous "+pair.Key+".");}
  Check(!Receive(Capability(),Payload(),"http://127.0.0.1:53210/")&&!Receive(Capability(),Payload(),version:3),"A different document route or newer draft invalidates the resize message.");
  var navPayload=Payload();navPayload["region"]="navigation";Check(Receive(Capability("navigation"),navPayload),"A Navigation capability accepts its own layout identity independently of Header.");
  var result="STUDIO_HEADER_RESIZE_OK|checks="+checks.Count;await File.WriteAllTextAsync(Path.Combine(output,"studio-header-resize-verification.json"),JsonSerializer.Serialize(new{result,checks},ContentJson.Options),ct);return result;
 }
}
