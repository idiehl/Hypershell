# Reproducible C# namespace inventory

The inventory is a namespace tree, not a list of classes arranged into a row.
Every dotted prefix is present, including 35 prefixes with no directly declared
public types. A child namespace does not become imported by importing its parent.

The versioned scope is the locally installed official Microsoft reference sets:

| Reference set | Version | API namespaces | Defined public type names |
| --- | --- | ---: | ---: |
| Microsoft.NETCore.App.Ref | 10.0.12 | 127 | 3,966 |
| Microsoft.AspNetCore.App.Ref | 10.0.12 | 211 | 3,007 |
| Microsoft.WindowsDesktop.App.Ref | 10.0.12 | 124 | 4,643 |
| .NET Framework full targeting pack, including Facades | 4.8.1 | 453 | 15,137 |

After deduplication across sets: **701 API namespaces, 736 tree entries, 19,384
defined type names, and 26,828 source type records**. Namespaces can occur in more
than one framework; the per-pack counts do not add to the deduplicated total.

C# permits application and third-party namespaces, so there is no finite set of
all C# namespaces. This inventory covers every nonempty namespace represented by
an externally visible type definition or exported public type in those exact
packs. It does not claim every NuGet package, mobile workload, historical assembly,
user namespace, or superseded API removed before .NET Framework 4.8.1.

`System.Web` needs special care: modern .NET includes `HttpUtility`, while its
ASP.NET server, Web Forms, hosting, and UI descendants belong to .NET Framework.
Modern ASP.NET Core appears under `Microsoft.AspNetCore`. A namespace shared by
frameworks does not imply all of its individual types are available in both.
Windows-specific APIs may require Windows even when classified as modern .NET.

## Regeneration

Run PowerShell 7 using the installed reference packs:

```powershell
./scripts/programming-trees/catalog-csharp.ps1
node scripts/programming-trees/catalog-csharp.mjs
```

The PowerShell extractor reads ECMA-335 metadata through `PEReader`; it never
loads or executes the target reference assemblies. Top-level public types and
externally accessible nested types are retained, with their original assembly,
framework, kind, and forwarding status. Every source DLL has a SHA-256 receipt.

The Node step adds all missing dotted prefixes, branch statistics, framework
availability, concise purpose labels, representative extracted types, and official
documentation destinations. The complete source type index is a CSV attachment.

Generated data is kept under `content/programming-trees-20261021/`:

- `csharp-namespaces.json` — hierarchy, descriptions, and source/assembly manifest.
- `CSharp-namespace-type-index.csv` — every extracted public source type record.
- `catalog-csharp.raw.json` — reproducible metadata extraction cache.
- `catalog-csharp.docs.json` — retrieved official namespace-summary provenance.

`node .../catalog-csharp.mjs --fetch-docs` optionally refreshes Microsoft Learn
summaries. It stops after a rate-limit response. `--fetch-namespace-xml` fetches
available official `dotnet/dotnet-api-docs` namespace summaries. Neither network
step determines namespace completeness: the reference metadata does. Namespace
summaries are capped at 24 source words; authored factual overviews and
metadata-derived finding aids are used elsewhere. A generated documentation URL
is explicitly separate from a page successfully retrieved during this run.

Do not silently update versions or modern/legacy classifications when revising
the Work. Regenerate and review the versioned manifest first.
