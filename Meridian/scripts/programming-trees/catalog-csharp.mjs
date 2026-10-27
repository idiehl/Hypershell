import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';

// Run catalog-csharp.ps1 first. --fetch-docs refreshes official namespace summaries.
// Other runs reuse the pinned summary cache and are deterministic.
const scriptDir = path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/,'$1'));
const projectDir = path.resolve(scriptDir,'../..');
const outputDir = path.join(projectDir,'content/programming-trees-20261021');
await fs.mkdir(outputDir,{recursive:true});
const raw = JSON.parse(await fs.readFile(path.join(outputDir,'catalog-csharp.raw.json'),'utf8'));
const actualNames = [...new Set(raw.types.map(t=>t.namespace))].sort();
const docsPath = path.join(outputDir,'catalog-csharp.docs.json');
const docs = await fs.readFile(docsPath,'utf8').then(JSON.parse).catch(()=>({}));
const decode = text => text.replace(/&quot;/g,'"').replace(/&apos;|&#39;/g,"'").replace(/&lt;/g,'<').replace(/&gt;/g,'>').replace(/&amp;/g,'&').replace(/&#(\d+);/g,(_,n)=>String.fromCodePoint(Number(n)));
const clean = text => decode(text.replace(/<see\b[^>]*cref="(?:[A-Z]:)?([^"]+)"[^>]*\/>/g,'$1').replace(/<[^>]+>/g,' ').replace(/\s+/g,' ').trim());
const excerpt = text => {
  const words=clean(text).split(/\s+/);
  if (!words.length || /^(To be added\.?|TBD)$/i.test(words.join(' '))) return '';
  return words.length>24 ? words.slice(0,24).join(' ')+'…' : words.join(' ');
};
function viewFor(ns) {
  const packs=new Set(raw.types.filter(t=>t.namespace===ns).map(t=>t.pack));
  return packs.has('net10-aspnet') && !packs.has('net10-runtime') && !packs.has('net10-windowsdesktop') ? 'aspnetcore-10.0' : [...packs].some(p=>p.startsWith('net10-')) ? 'net-10.0' : 'netframework-4.8.1';
}
const urlFor = ns => `https://learn.microsoft.com/en-us/dotnet/api/${ns.toLowerCase()}?view=${viewFor(ns)}`;
if (process.argv.includes('--fetch-docs')) {
  let next=0,done=0,rateLimited=false;
  await Promise.all(Array.from({length:2},async()=>{
    while (next<actualNames.length && !rateLimited) {
      const ns=actualNames[next++];
      if (docs[ns]?.status===200 && !process.argv.includes('--force')) { done++; continue; }
      const url=urlFor(ns);
      try {
        const response=await fetch(url,{signal:AbortSignal.timeout(20000)});
        const html=await response.text();
        const summary=html.match(/<div\b[^>]*class="[^"]*\bsummary\b[^"]*"[^>]*>([\s\S]*?)<\/div>/i)?.[1]??'';
        docs[ns]={url,status:response.status,excerpt:excerpt(summary),responseSha256:crypto.createHash('sha256').update(html).digest('hex'),retrieved:'2026-10-21'};
        if(response.status===429) rateLimited=true;
      } catch(error) { docs[ns]={url,status:0,excerpt:'',error:String(error)}; }
      done++;
      if(done%100===0) console.log(`Namespace documentation ${done}/${actualNames.length}`);
    }
  }));
  await fs.writeFile(docsPath,JSON.stringify(Object.fromEntries(Object.entries(docs).sort(([a],[b])=>a.localeCompare(b))),null,2)+'\n');
}
if (process.argv.includes('--fetch-namespace-xml')) {
  let next=0,done=0,rateLimited=false;
  const todo=actualNames.filter(ns=>!docs[ns]?.excerpt && !ns.startsWith('Microsoft.AspNetCore') && !ns.startsWith('Microsoft.Extensions'));
  await Promise.all(Array.from({length:4},async()=>{
    while(next<todo.length && !rateLimited) {
      const ns=todo[next++];
      const url=`https://raw.githubusercontent.com/dotnet/dotnet-api-docs/main/xml/ns-${ns}.xml`;
      try {
        const response=await fetch(url,{signal:AbortSignal.timeout(20000)});
        const xml=await response.text();
        const summary=xml.match(/<summary>([\s\S]*?)<\/summary>/i)?.[1]??'';
        docs[ns]={...docs[ns],namespaceXml:{url,status:response.status,sha256:crypto.createHash('sha256').update(xml).digest('hex'),retrieved:'2026-10-21'}};
        if(response.status===200) docs[ns].excerpt=excerpt(summary);
        if(response.status===429) rateLimited=true;
      } catch(error) { docs[ns]={...docs[ns],namespaceXml:{url,status:0,error:String(error)}}; }
      done++;
      if(done%100===0) console.log(`Namespace source summaries ${done}/${todo.length}`);
    }
  }));
  await fs.writeFile(docsPath,JSON.stringify(Object.fromEntries(Object.entries(docs).sort(([a],[b])=>a.localeCompare(b))),null,2)+'\n');
}
const purpose = {
  'System':'Core values, exceptions, events, application services, and commonly used runtime types.',
  'System.Collections':'Collection contracts and nongeneric containers; explore child namespaces for generic, concurrent, immutable, and frozen choices.',
  'System.Collections.Generic':'Strongly typed lists, dictionaries, queues, sets, and collection interfaces.',
  'System.Collections.Concurrent':'Thread-safe collections and producer/consumer coordination.',
  'System.Collections.Immutable':'Persistent collections that return new values when changed.',
  'System.Collections.Frozen':'Read-only lookup structures optimized after construction.',
  'System.ComponentModel':'Component metadata, binding, conversion, validation, and design-time integration.',
  'System.Data':'ADO.NET data tables, relations, datasets, and shared database abstractions.',
  'System.Diagnostics':'Processes, tracing, assertions, debugging, and performance inspection.',
  'System.DirectoryServices':'Windows directory-service integration; availability depends on the target platform and referenced libraries.',
  'System.Drawing':'Drawing primitives and Windows-oriented graphics; portable primitives and desktop drawing have different availability.',
  'System.Dynamic':'Dynamic-object behavior and runtime language binding contracts.',
  'System.Globalization':'Cultures, calendars, formatting, parsing, and language-aware text comparison.',
  'System.IO':'Files, directories, streams, and filesystem operations.',
  'System.IO.Compression':'Compression streams and archive formats.',
  'System.IO.Pipelines':'High-throughput buffered asynchronous input and output.',
  'System.Linq':'Language-integrated queries over sequences and query providers.',
  'System.Linq.Expressions':'Expression trees for representing and compiling executable code.',
  'System.Net':'Network requests, addressing, credentials, and protocol infrastructure.',
  'System.Net.Http':'HTTP requests, responses, clients, and message handlers.',
  'System.Net.Sockets':'Socket-level TCP, UDP, and local network communication.',
  'System.Numerics':'Arbitrary-precision, complex, vector, matrix, and generic numeric contracts.',
  'System.Reflection':'Inspect assemblies, types, members, attributes, and runtime metadata.',
  'System.Reflection.Emit':'Generate runtime types and intermediate-language instructions.',
  'System.Reflection.Metadata':'Read and construct CLI metadata without loading an assembly for execution.',
  'System.Resources':'Resource lookup, localization infrastructure, and resource readers.',
  'System.Runtime':'Runtime contracts and support APIs; child branches cover compilation, interop, loading, serialization, and versioning.',
  'System.Runtime.InteropServices':'Managed/native interoperation, marshalling, COM, and platform-call support.',
  'System.Runtime.Intrinsics':'Hardware intrinsic vectors and architecture-specific CPU instructions.',
  'System.Runtime.Remoting':'Legacy .NET Framework remoting infrastructure; it is not the modern .NET distributed application model.',
  'System.Runtime.Serialization':'Serialization contracts, callbacks, metadata, and data-contract infrastructure.',
  'System.Runtime.Serialization.Formatters.Binary':'Historical binary-serialization APIs. BinaryFormatter is unsafe and unsupported for new designs; consult current migration guidance.',
  'System.Security':'Security contracts and framework integration; permissions-era APIs and modern cryptography have different support.',
  'System.Security.Cryptography':'Hashing, encryption, signatures, keys, random values, and cryptographic primitives.',
  'System.Security.Cryptography.X509Certificates':'X.509 certificates, stores, certificate requests, and chain validation.',
  'System.Text':'Text encodings, mutable strings, Unicode values, and text transformation.',
  'System.Text.Json':'Read, write, inspect, serialize, and deserialize UTF-8 JSON.',
  'System.Text.Json.Nodes':'Mutable JSON document nodes for editing object and array structures.',
  'System.Text.Json.Serialization':'Customize JSON serialization using attributes, converters, contexts, and metadata.',
  'System.Text.Json.Schema':'Generate and configure JSON Schema descriptions from serialization metadata.',
  'System.Text.RegularExpressions':'Pattern-based text matching, extraction, replacement, and splitting.',
  'System.Threading':'Threads, synchronization, cancellation, timers, and coordination primitives.',
  'System.Threading.Tasks':'Tasks, asynchronous workflows, parallel operations, and scheduling.',
  'System.Threading.Channels':'Asynchronous producer/consumer channels.',
  'System.Threading.RateLimiting':'Concurrency and throughput limits using leases and rate-limiter policies.',
  'System.Transactions':'Transaction scopes, enlistment, and transaction coordination.',
  'System.Xml':'XML readers, writers, document models, namespaces, and processing.',
  'System.Xml.Linq':'LINQ-friendly XML elements, attributes, and documents.',
  'System.Xml.XPath':'XPath expression evaluation and navigators.',
  'System.Xml.Xsl':'XSLT stylesheet transformation and related processing.',
  'System.Web':'Modern .NET retains HttpUtility here. The ASP.NET server, Web Forms, hosting, and UI branches belong to .NET Framework.',
  'System.Web.UI':'Legacy ASP.NET Web Forms controls, pages, and server-rendered UI.',
  'System.Web.Services':'Legacy ASP.NET XML web services and SOAP infrastructure.',
  'System.ServiceModel':'Windows Communication Foundation service contracts and communication infrastructure in the .NET Framework scope.',
  'System.Activities':'Windows Workflow Foundation activities and workflow execution in the .NET Framework scope.',
  'System.Workflow':'The older Workflow Foundation API family, preserved for historical framework projects.',
  'System.Windows':'Windows Presentation Foundation application, layout, dependency-property, and UI infrastructure.',
  'System.Windows.Controls':'WPF controls, content presentation, panels, and selectors.',
  'System.Windows.Media':'WPF brushes, geometries, transforms, drawing, and multimedia presentation.',
  'System.Windows.Forms':'Windows Forms controls, dialogs, input, layout, and application integration.',
  'System.Xaml':'XAML object reading, writing, schema, and type-system services.',
  'Microsoft':'Microsoft library families. This prefix contains no directly declared public types in the selected packs.',
  'Microsoft.AspNetCore':'ASP.NET Core application startup and the parent branch for modern web-server APIs.',
  'Microsoft.AspNetCore.Authentication':'Authentication schemes, handlers, tickets, and sign-in services.',
  'Microsoft.AspNetCore.Authorization':'Authorization requirements, policies, handlers, and evaluation.',
  'Microsoft.AspNetCore.Builder':'Application, endpoint, middleware, and service-pipeline configuration extensions.',
  'Microsoft.AspNetCore.Components':'Blazor components, rendering, parameter binding, navigation, and component lifecycle.',
  'Microsoft.AspNetCore.Http':'HTTP context, requests, responses, cookies, results, and endpoint metadata.',
  'Microsoft.AspNetCore.Hosting':'Web hosts, server environments, startup, and application lifecycle.',
  'Microsoft.AspNetCore.Identity':'ASP.NET Core users, roles, sign-in, tokens, and identity management.',
  'Microsoft.AspNetCore.Mvc':'Controllers, action results, model binding, filters, views, and MVC integration.',
  'Microsoft.AspNetCore.Routing':'Endpoint routing, route patterns, constraints, matching, and URL generation.',
  'Microsoft.AspNetCore.SignalR':'Real-time hubs, client proxies, connection management, and messaging.',
  'Microsoft.Extensions':'Shared application infrastructure used by web, desktop, service, and console applications.',
  'Microsoft.Extensions.Configuration':'Layered configuration providers, values, binding, and change notification.',
  'Microsoft.Extensions.DependencyInjection':'Service registration, dependency resolution, scopes, and service lifetimes.',
  'Microsoft.Extensions.Hosting':'Generic host startup, background services, environments, and application lifetime.',
  'Microsoft.Extensions.Logging':'Structured logging contracts, categories, levels, providers, and factories.',
  'Microsoft.Extensions.Options':'Strongly typed options, validation, named options, snapshots, and change monitoring.',
  'Microsoft.Extensions.Caching':'Memory, distributed, and hybrid application caching families.',
  'Microsoft.Build':'MSBuild project construction, evaluation, execution, tasks, logging, and build extensibility.',
  'Microsoft.CSharp':'C# code generation and dynamic-binding support from the selected framework libraries.',
  'Microsoft.VisualBasic':'Visual Basic runtime APIs exposed to .NET languages, including C# when referenced.',
  'Microsoft.Win32':'Windows registry, safe-handle, and operating-system integration APIs.',
  'Accessibility':'COM accessibility contracts used by Windows user-interface components.',
  'UIAutomationClientsideProviders':'Windows UI Automation client-side provider registration.',
  'XamlGeneratedNamespace':'Public generated XAML helper metadata in the reference set; this is not a general application API family.'
};
const preferred = {
  'System':['Object','String','Console','DateTime','Math'],
  'System.Text':['StringBuilder','Encoding','Rune'],
  'System.Text.Json':['JsonSerializer','JsonDocument','Utf8JsonReader','Utf8JsonWriter'],
  'System.Collections.Generic':['List`1','Dictionary`2','HashSet`1','IEnumerable`1'],
  'System.IO':['File','Directory','Stream','FileStream'],
  'System.Net.Http':['HttpClient','HttpRequestMessage','HttpResponseMessage'],
  'System.Threading.Tasks':['Task','Task`1','Parallel','ValueTask'],
  'System.Web':['HttpUtility','HttpContext','HttpRequest'],
  'Microsoft.AspNetCore.Mvc':['Controller','ControllerBase','IActionResult'],
  'Microsoft.AspNetCore.Components':['ComponentBase','IComponent','ParameterAttribute'],
  'Microsoft.Extensions.DependencyInjection':['IServiceCollection','ServiceCollection','ServiceProvider'],
  'System.Windows':['Application','Window','FrameworkElement','DependencyObject'],
  'System.Windows.Forms':['Form','Control','Application','Button']
};
const allNames=new Set(actualNames);
for(const ns of actualNames) {
  const pieces=ns.split('.');
  while(pieces.length>1) { pieces.pop(); allNames.add(pieces.join('.')); }
}
const byNamespace=new Map();
for(const row of raw.types) {
  if(!byNamespace.has(row.namespace)) byNamespace.set(row.namespace,[]);
  byNamespace.get(row.namespace).push(row);
}
const names=[...allNames].sort();
const entries=names.map(ns=>{
  const rows=byNamespace.get(ns)??[];
  const defined=rows.filter(t=>!t.forwarded);
  const typeRows=defined.length?defined:rows;
  const uniqueTypes=[...new Map(typeRows.map(t=>[t.fullName,t])).values()];
  const packs=[...new Set(rows.map(t=>t.pack))].sort();
  const modern=packs.some(p=>p.startsWith('net10-'));
  const legacy=packs.includes('netframework481');
  const ordered=uniqueTypes.sort((a,b)=>{
    const p=preferred[ns]??[];
    const ai=p.indexOf(a.name),bi=p.indexOf(b.name);
    if(ai>=0||bi>=0) return (ai<0?999:ai)-(bi<0?999:bi);
    const penalty=t=> (t.fullName.slice(ns.length+1).includes('.')?20:0)+(t.kind==='delegate'?5:t.kind==='enum'?4:0)+(/Attribute$|Exception$|EventArgs$/.test(t.name)?3:0)+t.name.length/100;
    return penalty(a)-penalty(b)||a.fullName.localeCompare(b.fullName);
  });
  const children=names.filter(n=>n.startsWith(ns+'.')&&n.split('.').length===ns.split('.').length+1);
  const representativeTypes=ordered.slice(0,4).map(t=>({name:t.name,fullName:t.fullName,kind:t.kind,documentationUrl:`https://learn.microsoft.com/en-us/dotnet/api/${t.fullName.toLowerCase().replaceAll('`','-')}?view=${viewFor(ns)}`}));
  const description=purpose[ns]??docs[ns]?.excerpt??'';
  const fallback=rows.length?`APIs include ${representativeTypes.map(t=>t.name.replace(/`\d+/g,'<>')).slice(0,3).join(', ')}. Explore the child branches for more specialized namespaces.`:`Namespace prefix grouping ${children.length} immediate branches; no public types are declared directly at this prefix in the selected packs.`;
  return {name:ns,segment:ns.split('.').at(-1),parent:ns.includes('.')?ns.slice(0,ns.lastIndexOf('.')):null,depth:ns.split('.').length,prefixOnly:rows.length===0,description:description||fallback,descriptionKind:purpose[ns]?'authored factual overview':docs[ns]?.excerpt?'Microsoft namespace summary excerpt (at most 24 words)':'metadata-derived finding aid',descriptionSource:purpose[ns]?null:docs[ns]?.excerpt?(docs[ns]?.namespaceXml?.status===200?docs[ns].namespaceXml.url:docs[ns].url):null,documentationUrl:rows.length?urlFor(ns):null,documentationVerified:docs[ns]?.status===200,frameworks:packs,modern,legacy,availability:modern&&legacy?'Modern .NET and .NET Framework':modern?'Modern .NET':'NET Framework 4.8.1 only in this snapshot',publicTypeCount:uniqueTypes.length,modernPublicTypeCount:new Set(defined.filter(t=>t.pack.startsWith('net10-')).map(t=>t.fullName)).size,frameworkPublicTypeCount:new Set(defined.filter(t=>t.pack==='netframework481').map(t=>t.fullName)).size,assemblies:[...new Set(rows.map(t=>t.assembly))].sort(),representativeTypes,children,descendantCount:names.filter(n=>n.startsWith(ns+'.')).length};
});
for(const entry of entries.filter(e=>e.prefixOnly)) {
  const descendants=entries.filter(e=>e.name.startsWith(entry.name+'.'));
  entry.frameworks=[...new Set(descendants.flatMap(e=>e.frameworks))].sort();
  entry.modern=descendants.some(e=>e.modern); entry.legacy=descendants.some(e=>e.legacy);
  entry.availability='Namespace prefix; inspect descendants for framework availability';
}
const uniqueTypeNames=new Set(raw.types.filter(t=>!t.forwarded).map(t=>t.fullName));
const allTypeRows=[...new Map(raw.types.map(t=>[[t.pack,t.assembly,t.fullName,t.forwarded].join('|'),t])).values()].sort((a,b)=>a.namespace.localeCompare(b.namespace)||a.fullName.localeCompare(b.fullName)||a.pack.localeCompare(b.pack)||a.assembly.localeCompare(b.assembly));
const csvCell=x=>`"${String(x).replaceAll('"','""')}"`;
const csv=['Namespace,Type,Kind,Framework pack,Assembly,Forwarded',...allTypeRows.map(t=>[t.namespace,t.fullName,t.kind,t.pack,t.assembly,t.forwarded].map(csvCell).join(','))].join('\r\n')+'\r\n';
const csvPath=path.join(outputDir,'CSharp-namespace-type-index.csv');
await fs.writeFile(csvPath,csv);
const packs=raw.packs.map(p=>({...p,namespaceCount:new Set(raw.types.filter(t=>t.pack===p.id).map(t=>t.namespace)).size,definedTypeCount:new Set(raw.types.filter(t=>t.pack===p.id&&!t.forwarded).map(t=>t.fullName)).size}));
const catalog={schemaVersion:1,title:'C# namespaces',snapshotDate:'2026-10-21',scope:'Every nonempty namespace with an externally visible type definition or exported public type in Microsoft.NETCore.App.Ref, Microsoft.AspNetCore.App.Ref, and Microsoft.WindowsDesktop.App.Ref 10.0.12, plus the .NET Framework 4.8.1 full targeting pack and its Facades directory.',scopeNote:'C# permits arbitrary namespaces declared by applications and third-party libraries, so there is no finite list of every C# namespace. This is a complete, reproducible namespace inventory of the stated Microsoft reference packs, not all NuGet packages, workloads, historical assemblies, or user-defined namespaces. .NET Framework 4.8.1 preserves older framework APIs; superseded APIs no longer in that targeting pack are outside this inventory.',treeMeaning:'Dotted names form a namespace-prefix hierarchy. A child namespace is not imported by a using directive for its parent; the tree is not class inheritance or assembly containment. Prefix-only nodes are labeled and may have no directly declared public types.',availabilityNote:'A namespace may exist in both frameworks while individual types differ. Modern/Framework counts indicate extracted public type names, not universal cross-platform support. Internal-named public infrastructure is retained for complete metadata coverage; documentation may discourage direct use.',statistics:{namespaceCount:actualNames.length,prefixCount:names.length-actualNames.length,treeNodeCount:entries.length,uniqueDefinedTypeNames:uniqueTypeNames.size,typeRecords:allTypeRows.length,documentationPagesVerified:actualNames.filter(n=>docs[n]?.status===200).length,descriptionExcerptCount:entries.filter(e=>e.descriptionKind.startsWith('Microsoft')).length,roots:entries.filter(e=>!e.parent).map(e=>e.name)},sources:[{title:'C# namespaces and using directives',url:'https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/program-structure/namespaces'},{title:'.NET Framework versions and dependencies',url:'https://learn.microsoft.com/en-us/dotnet/framework/install/versions-and-dependencies'},{title:'System.Text.Json namespace',url:'https://learn.microsoft.com/en-us/dotnet/api/system.text.json?view=net-10.0'},{title:'System.Web namespace and framework distinction',url:'https://learn.microsoft.com/en-us/dotnet/api/system.web?view=netframework-4.8.1'},{title:'ASP.NET Core MVC namespace',url:'https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc?view=aspnetcore-10.0'},{title:'BinaryFormatter migration guidance',url:'https://learn.microsoft.com/en-us/dotnet/standard/serialization/binaryformatter-migration-guide/'}],packs,namespaceEntries:entries,typeIndex:{path:'content/programming-trees-20261021/CSharp-namespace-type-index.csv',fileName:'CSharp-namespace-type-index.csv',mimeType:'text/csv',bytes:Buffer.byteLength(csv),sha256:crypto.createHash('sha256').update(csv).digest('hex'),rows:allTypeRows.length},extraction:raw.extraction};
assert.equal(new Set(entries.map(e=>e.name)).size,entries.length);
assert(entries.some(e=>e.name==='System.Text.Json'&&e.parent==='System.Text'));
assert(entries.some(e=>e.name==='System.Web.UI'&&!e.modern&&e.legacy));
assert(entries.some(e=>e.name==='Microsoft.AspNetCore.Mvc'&&e.modern&&!e.legacy));
assert(entries.filter(e=>!e.prefixOnly).length===actualNames.length);
assert(entries.every(e=>e.parent===null||allNames.has(e.parent)));
assert(entries.every(e=>e.description.length>0));
await fs.writeFile(path.join(outputDir,'csharp-namespaces.json'),JSON.stringify(catalog,null,2)+'\n');
console.log(JSON.stringify(catalog.statistics,null,2));
console.log('CSHARP_NAMESPACE_CATALOG_OK');
