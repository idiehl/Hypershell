param(
  [string]$PackRoot = 'C:\Program Files\dotnet\packs',
  [string]$Version = '10.0.12',
  [string]$FrameworkRoot = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8.1',
  [string]$Output = (Join-Path $PSScriptRoot '..\..\content\programming-trees-20261021\catalog-csharp.raw.json')
)
$ErrorActionPreference = 'Stop'
# Reads ECMA-335 metadata without loading or executing any reference assembly.
Add-Type -ReferencedAssemblies 'System.Reflection.Metadata','System.Collections.Immutable','System.Collections','System.Runtime' -TypeDefinition @'
using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
public static class MeridianNamespaceCatalog {
  public sealed class Row {
    public string Namespace, Name, FullName, Kind;
    public bool Forwarded;
    public Row(string ns, string name, string fullName, string kind, bool forwarded) { Namespace=ns; Name=name; FullName=fullName; Kind=kind; Forwarded=forwarded; }
  }
  public static Row[] Read(string path) {
    using var stream = File.OpenRead(path);
    using var pe = new PEReader(stream);
    if (!pe.HasMetadata) return Array.Empty<Row>();
    var reader = pe.GetMetadataReader();
    var rows = new List<Row>();
    bool Visible(TypeDefinitionHandle h) {
      var d = reader.GetTypeDefinition(h);
      var v = d.Attributes & TypeAttributes.VisibilityMask;
      if (d.IsNested) return (v == TypeAttributes.NestedPublic || v == TypeAttributes.NestedFamily || v == TypeAttributes.NestedFamORAssem) && Visible(d.GetDeclaringType());
      return v == TypeAttributes.Public;
    }
    string Full(TypeDefinitionHandle h) {
      var d = reader.GetTypeDefinition(h);
      var name = reader.GetString(d.Name);
      if (d.IsNested) return Full(d.GetDeclaringType()) + "." + name;
      var ns = reader.GetString(d.Namespace);
      return ns.Length == 0 ? name : ns + "." + name;
    }
    string Namespace(TypeDefinitionHandle h) {
      var d = reader.GetTypeDefinition(h);
      return d.IsNested ? Namespace(d.GetDeclaringType()) : reader.GetString(d.Namespace);
    }
    string BaseName(EntityHandle h) {
      if (h.IsNil) return "";
      if (h.Kind == HandleKind.TypeReference) {
        var r = reader.GetTypeReference((TypeReferenceHandle)h);
        return reader.GetString(r.Namespace) + "." + reader.GetString(r.Name);
      }
      if (h.Kind == HandleKind.TypeDefinition) return Full((TypeDefinitionHandle)h);
      return "";
    }
    foreach (var h in reader.TypeDefinitions) {
      if (!Visible(h)) continue;
      var d = reader.GetTypeDefinition(h);
      var ns = Namespace(h);
      if (ns.Length == 0) continue;
      var baseName = BaseName(d.BaseType);
      var kind = (d.Attributes & TypeAttributes.Interface) != 0 ? "interface" : baseName == "System.Enum" ? "enum" : baseName == "System.ValueType" ? "struct" : baseName == "System.MulticastDelegate" ? "delegate" : "class";
      rows.Add(new Row(ns,reader.GetString(d.Name),Full(h),kind,false));
    }
    foreach (var h in reader.ExportedTypes) {
      var d = reader.GetExportedType(h);
      var ns = reader.GetString(d.Namespace);
      if (ns.Length == 0 || (d.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public) continue;
      var name = reader.GetString(d.Name);
      rows.Add(new Row(ns,name,ns+"."+name,"forwarded type",true));
    }
    return rows.ToArray();
  }
}
'@
$specs = @(
  @{ id='net10-runtime'; name='.NET 10 base class library'; package='Microsoft.NETCore.App.Ref'; version=$Version; root=(Join-Path $PackRoot "Microsoft.NETCore.App.Ref\$Version\ref\net10.0"); source="https://www.nuget.org/packages/Microsoft.NETCore.App.Ref/$Version" },
  @{ id='net10-aspnet'; name='ASP.NET Core 10'; package='Microsoft.AspNetCore.App.Ref'; version=$Version; root=(Join-Path $PackRoot "Microsoft.AspNetCore.App.Ref\$Version\ref\net10.0"); source="https://www.nuget.org/packages/Microsoft.AspNetCore.App.Ref/$Version" },
  @{ id='net10-windowsdesktop'; name='.NET 10 Windows Desktop'; package='Microsoft.WindowsDesktop.App.Ref'; version=$Version; root=(Join-Path $PackRoot "Microsoft.WindowsDesktop.App.Ref\$Version\ref\net10.0"); source="https://www.nuget.org/packages/Microsoft.WindowsDesktop.App.Ref/$Version" },
  @{ id='netframework481'; name='.NET Framework 4.8.1'; package='Microsoft .NET Framework 4.8.1 targeting pack'; version='4.8.1'; root=$FrameworkRoot; source='https://learn.microsoft.com/en-us/dotnet/framework/install/guide-for-developers' }
)
$allTypes = [Collections.Generic.List[object]]::new()
$manifests = [Collections.Generic.List[object]]::new()
foreach ($spec in $specs) {
  if (!(Test-Path -LiteralPath $spec.root)) { throw "Reference pack not found: $($spec.root)" }
  $files = @(Get-ChildItem -LiteralPath $spec.root -Filter '*.dll' -File)
  $facades = Join-Path $spec.root 'Facades'
  if (Test-Path -LiteralPath $facades) { $files += @(Get-ChildItem -LiteralPath $facades -Filter '*.dll' -File) }
  $assemblies = @()
  $start = $allTypes.Count
  foreach ($file in ($files | Sort-Object FullName)) {
    try { $rows = [MeridianNamespaceCatalog]::Read($file.FullName) }
    catch { throw "Metadata extraction failed for $($file.FullName): $_" }
    foreach ($row in $rows) {
      $allTypes.Add(@{ namespace=$row.Namespace; name=$row.Name; fullName=$row.FullName; kind=$row.Kind; forwarded=$row.Forwarded; pack=$spec.id; assembly=$file.BaseName })
    }
    $assemblies += @{ name=$file.Name; relativePath=[IO.Path]::GetRelativePath($spec.root,$file.FullName); bytes=$file.Length; sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); publicTypeRecords=$rows.Length }
  }
  $manifests.Add(@{ id=$spec.id; name=$spec.name; package=$spec.package; version=$spec.version; root=$spec.root; source=$spec.source; assemblies=$assemblies; typeRecordCount=($allTypes.Count-$start) })
}
$payload = @{ schemaVersion=1; extractedAt=(Get-Date -Format 'yyyy-MM-dd'); extraction='ECMA-335 TypeDefinition and ExportedType metadata; public/protected nested types only when all declaring types are externally visible; no assembly execution'; packs=$manifests; types=$allTypes.ToArray() }
$payload | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $Output -Encoding utf8NoBOM
Write-Output "CATALOG_METADATA_OK $($allTypes.Count) type records across $($manifests.Count) packs: $Output"
