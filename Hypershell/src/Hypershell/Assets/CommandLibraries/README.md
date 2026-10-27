# Hyperlib command catalogs

Hypershell uses provenance-based Hyperlib collections. A language is not one
large "Essentials" bucket:

- `<Language> Core` owns syntax, keywords, operators, literals, control flow,
  and language-defined built-ins available without importing another library.
- Imported APIs belong to their canonical standard library, specification,
  platform API, framework, module, or package.
- One public symbol has one canonical owning collection. Cross-references are
  valid; duplicate copies in Core and an imported library are not.

The complete generated coverage table is in
`doc/HYPERLIB_COVERAGE.md`. Runtime-discovered PowerShell and .NET catalogs are
reported by the application because their exact contents depend on the host.

## Schema v2 layout

Schema v2 separates three payloads:

1. A small descriptor (`<library-id>.json`) is loaded at startup. It records
   kind, ecosystem, canonical name, target/source version, provenance,
   licensing, coverage counts, migration aliases, and shard paths.
2. Compact index shards under `entries/` are loaded only after the user selects
   a collection. Every entry records `symbolKind`, `coverageTier`, and a generic
   hierarchical `browsePath`.
3. Detailed explanations, parameters, and examples under `guides/` are loaded
   only when their entry is opened.

Index shards are compact JSON and limited to 5,000 generated entries apiece.
This keeps startup descriptor-only even for the 52,000-entry Java SE index.

Coverage tiers are:

- `Indexed`: signature, canonical hierarchy, and authoritative documentation
  link.
- `Guided`: Indexed metadata plus an original explanation and useful example.
- `Verified`: Guided content whose supported-runtime verification policy has
  passed and has been explicitly promoted.

Schema-v1 catalogs remain loadable for compatibility. New official content and
new authoring work should use schema v2.

## Shipped collections

The generated static catalogs currently include:

- C# Core;
- Python Core and the complete documented Python 3.14 Standard Library
  inventory, including 68 detailed guides across core language features and
  the foundational `pathlib`, `os`, `sys`, `json`, `datetime`, `re`,
  `collections`, `itertools`, `subprocess`, and `asyncio` modules;
- JavaScript Core from pinned ECMAScript declaration metadata and the complete
  documented Node.js 24 Standard Library inventory, excluding browser APIs;
- TypeScript Core with 45 parameterized, runtime-verified language and
  type-system guides plus global utility types for the TypeScript 7 language
  surface, and a distinct 6,779-symbol TypeScript 6.0.3 compatibility API
  collection with 33 detailed guides;
- Java Core and the Java SE 25 module/package/type/member/overload inventory;
- C++ Core and the C++ Standard Library organized by header, namespace, type or
  function, member, and overload;
- HTML Core elements and attributes from the WHATWG HTML index;
- CSS Core plus canonical Flexbox, Grid, Fonts, Transitions, Animations, Color,
  and Transforms specification collections;
- separate DOM, Fetch, URL, Streams, Storage, XHR/FormData, Workers, Canvas,
  Web Crypto, timers, and navigation platform surfaces.

The default language profiles also include a deliberately small, curated
popular-library layer. These are guided starter recipes rather than vendored
dependencies or claims of complete API coverage:

- PowerShell: Pester, PSScriptAnalyzer, and Microsoft Graph PowerShell;
- C#: Newtonsoft.Json, Serilog, and Dapper;
- C++: Boost, {fmt}, and nlohmann/json;
- Python: Requests, NumPy, and pandas;
- JavaScript: React, Express, and Lodash;
- TypeScript: Zod, Prisma ORM, and RxJS;
- Java: Spring Boot, Jackson Databind, and JUnit Jupiter;
- HTML: Bootstrap, htmx, and Alpine.js;
- CSS: Tailwind CSS, Dart Sass, and Bulma;
- Web APIs: D3, three.js, and Workbox.

Every collection records its canonical project, target version, primary
documentation, and license. Hypershell never installs these packages. Recipes
that can use an already-configured PowerShell, Python, or Node.js environment
may be staged or run; project-only, compiled, JSX, build-tool, and browser
recipes remain editor guidance so the isolated runners do not imply dependency
resolution they do not provide.

PowerShell Core is discovered from the embedded PowerShell runtime and retains
its native module plus Verb-Noun navigation. Installed .NET Base Class Library
surfaces are discovered from each installed reference pack and remain separate
from C# Core.

## Navigation and runtime versions

Non-PowerShell catalogs use the shared hierarchy navigator. A collection may
expose labels such as MODULE, HEADER, PACKAGE, TYPE, MEMBER, and OVERLOAD from
its actual `browsePath`. Search is scoped to the selected collection and opens
the matching path directly. PowerShell retains module and Verb-Noun behavior.

Catalog descriptors always display their bundled target. When a compatible
Python, Node.js, JDK, or C++ toolchain is detected, the surface also identifies
the installed version. Otherwise the descriptor clearly says that the bundled
baseline index is active.

Static content never installs or launches its own executable. The Command
Builder derives each language's composition vocabulary and primary destination
from the host registry: terminal-oriented snippets may run, while source- and
document-oriented content inserts into a compatible open editor or creates a
correctly typed untitled document. Execution routes remain host-owned and
capability-gated:

- Python -> detected Python terminal;
- JavaScript -> detected Node.js terminal;
- TypeScript -> Node.js native type stripping;
- Java -> isolated `javac` plus `java` execution;
- C++ -> isolated C++20 compile/run through detected Clang, GCC, or MSVC;
- HTML, CSS, and Web APIs -> compatible editor insertion only.

## Migration

Retired library IDs such as `python-essentials` resolve through descriptor
`legacyIds` to their canonical replacement. Entry IDs retained from schema v1
continue to resolve after their library moves; changed entries may declare
entry-level `legacyIds`. The host rejects migration aliases that collide with
another canonical ID or alias in a content package.

The old Hypershell-authored C#/.NET draft files (`csharp.json`,
`csharp-language.json`, `csharp-streams.json`, `csharp-runtime.json`,
`csharp-concurrency.json`, and `csharp-networking.json`) are reserved and
excluded from distribution. They are not relabeled as official .NET APIs.

## External packages

The preferred extension format is an inert `ContentOnly` `.hyperlib` package
installed from **Settings > Extensions**. Packages may contribute schema-v1 or
schema-v2 libraries, but do not gain arbitrary process execution. Executable
entries must target a host-owned language route; otherwise they remain valid
guidance/editor content with `isExecutionSupported: false`.

See:

- `doc/HYPERLIB_PACKAGES.md`
- `doc/schemas/hyperlib-command-library-v2.schema.json`
- `doc/schemas/hyperlib-command-entry-shard-v2.schema.json`
- `doc/schemas/hyperlib-command-guide-shard-v2.schema.json`
- `doc/schemas/hyperlib-command-library.schema.json` (schema v1)

## Generation and verification

From the repository root:

```powershell
node tools\import-python-doc-inventory.mjs
node tools\import-node-doc-inventory.mjs
node tools\import-ecmascript-declarations.mjs
node tools\import-java-se-doc-inventory.mjs
node tools\import-java-language-index.mjs
node tools\import-html-standard-index.mjs
node tools\import-css-property-inventory.mjs
node tools\import-web-platform-declarations.mjs
node tools\import-typescript-compiler-api.mjs
node tools\import-cpp-language-index.mjs
node tools\import-cppreference-cpp20.mjs
node tools\generate-official-language-packs.mjs
node tools\generate-popular-library-starters.mjs
node tools\generate-hyperlib-coverage-report.mjs

node tools\generate-popular-library-starters.mjs --check

dotnet run --project tools\Hypershell.CSharpVerifier\Hypershell.CSharpVerifier.csproj `
  --configuration Release -- --hyperlib-packages-only
dotnet run --project tools\Hypershell.CSharpVerifier\Hypershell.CSharpVerifier.csproj `
  --configuration Release -- --official-language-packs-only
dotnet run --project tools\Hypershell.CSharpVerifier\Hypershell.CSharpVerifier.csproj `
  --configuration Release
```

Importers pin their upstream version where one exists and record source hashes.
Generation is deterministic and fails on ownership, coverage-count, hierarchy,
or shard drift. Hypershell-authored Python, JavaScript/Node.js, and TypeScript
guide material lives in the corresponding `*-guide-expansion.mjs` files under
`tools/catalog-sources`; upgrading an indexed symbol there retains its
canonical entry ID instead of creating a duplicate. The official-pack verifier
executes every Python, JavaScript/Node.js, and TypeScript entry marked
`Verified` through its supported host-owned runtime in addition to checking
templates for syntax.
