using System.Diagnostics;
using System.Text.Json;
using Meridian.TaskLauncher;

var passed=new List<string>();
void Check(string name,bool condition) { if(!condition) throw new Exception(name); passed.Add(name); }
void Reject(string name,Action action) { var rejected=false; try{action();}catch{rejected=true;} Check(name,rejected); }
var artifact=Path.GetFullPath(args.Single());
var manifest=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(File.ReadAllText(Path.Combine(artifact,"build-manifest.json")))!;
var metadata=new Dictionary<string,string>{["MeridianRoot"]=manifest["sourceRoot"].GetString()!,["BackendPowerShell"]=manifest["backendPowerShell"].GetString()!,["SecurePowerShell"]=manifest["securePowerShell"].GetString()!,["PowerShellSha256"]=manifest["powerShellSha256"].GetString()!};
foreach(var mode in new[]{"backend","secure-tunnel"})
{
    var plan=TaskLaunch.Create(mode,metadata);
    Check(mode+" uses CreateNoWindow",plan.CreateNoWindow);
    Check(mode+" bypasses shell and Terminal",!plan.UseShellExecute);
    Check(mode+" hides window",plan.WindowStyle==ProcessWindowStyle.Hidden);
    Check(mode+" has four fixed PowerShell arguments",plan.ArgumentList.Count==4 && plan.ArgumentList[0]=="-NoProfile" && plan.ArgumentList[1]=="-NonInteractive" && plan.ArgumentList[2]=="-File");
    Check(mode+" uses exact script",plan.ArgumentList[3]==Path.Combine(metadata["MeridianRoot"],"integrations","mcp","scripts",mode=="backend"?"run-backend-task.ps1":"run-secure-tunnel-task.ps1"));
}
Reject("Arbitrary command rejected",()=>TaskLaunch.Create("-Command calc",metadata));
Reject("Arbitrary path rejected",()=>TaskLaunch.Create("C:\\Windows\\System32\\cmd.exe",metadata));
Reject("Relative path rejected",()=>TaskLaunch.RegularPath("../pwsh.exe"));
var changed=new Dictionary<string,string>(metadata){["PowerShellSha256"]=new string('0',64)};
Reject("Changed PowerShell rejected before execution",()=>TaskLaunch.Create("backend",changed));
changed=new Dictionary<string,string>(metadata){["SecurePowerShell"]=metadata["BackendPowerShell"]};
Reject("Different profile PowerShell location rejected",()=>TaskLaunch.Create("secure-tunnel",changed));
var child=new ProcessStartInfo(metadata["SecurePowerShell"]){UseShellExecute=false,CreateNoWindow=true};
child.ArgumentList.Add("-NoProfile");child.ArgumentList.Add("-NonInteractive");child.ArgumentList.Add("-Command");child.ArgumentList.Add("Start-Sleep -Milliseconds 180; exit 37");
var clock=Stopwatch.StartNew();
Check("Child exit code propagated",TaskLaunch.StartAndWait(child)==37);
Check("Launcher waits for child lifetime",clock.ElapsedMilliseconds>=180);
var launcher=new ProcessStartInfo(Path.Combine(artifact,"Meridian.TaskLauncher.exe")){UseShellExecute=false,CreateNoWindow=true};
launcher.ArgumentList.Add("backend");launcher.ArgumentList.Add("-Command");
Check("Extra executable arguments rejected",TaskLaunch.StartAndWait(launcher)==64);
var sentinel="sk-sentinel-test-"+Guid.NewGuid().ToString("N");
Check("Full runtime-key shape accepted",KeySetup.IsRuntimeKey(sentinel));
Check("Tunnel ID rejected as a key",!KeySetup.IsRuntimeKey("tunnel_"+new string('a',32)));
Check("Tracking identifier rejected as a key",!KeySetup.IsRuntimeKey("tracking-"+new string('a',32)));
Check("Whitespace rejected from key input",!KeySetup.IsRuntimeKey("sk-with whitespace in key"));
Check("Quoted key rejected",!KeySetup.IsRuntimeKey("sk-key-with-quote-\"rejected"));
var cipher=KeySetup.Protect(sentinel);
Check("Key protection produces DPAPI hex",cipher.StartsWith("01000000",StringComparison.Ordinal)&&cipher.All(Uri.IsHexDigit)&&!cipher.Contains(sentinel));
Check("Key protection uses randomized ciphertext",cipher!=KeySetup.Protect(sentinel));
var roundtrip=new ProcessStartInfo(metadata["SecurePowerShell"]){UseShellExecute=false,CreateNoWindow=true};
roundtrip.ArgumentList.Add("-NoProfile");roundtrip.ArgumentList.Add("-NonInteractive");roundtrip.ArgumentList.Add("-Command");
roundtrip.ArgumentList.Add("$s=ConvertTo-SecureString '"+cipher+"';$p=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($s);try{if([Runtime.InteropServices.Marshal]::PtrToStringBSTR($p) -ceq '"+sentinel+"'){exit 0}else{exit 41}}finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($p);$s.Dispose()}");
Check("Encrypted GUI format decrypts through PowerShell ConvertTo-SecureString",TaskLaunch.StartAndWait(roundtrip)==0);
Console.WriteLine(JsonSerializer.Serialize(new{passed=passed.Count,checks=passed,liveTasksTouched=false,keysStored=false,networkCalls=false}));
