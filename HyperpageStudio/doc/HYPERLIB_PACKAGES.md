# Hyperlib package lifecycle and schema v2

Hypershell can validate, install, update, remove, and create deterministic
`.hyperlib` archives from **Settings > Extensions**. Packages are inert
`ContentOnly` extensions: library content never grants WebView, native-code,
filesystem, terminal, network, secret, panel, or AI capabilities by itself.

## User workflow

- **Install archive** validates and installs a `.hyperlib` file.
- **Install folder** validates an unpacked authoring directory.
- **Pack folder** creates a deterministic archive after full validation.
- **Remove package** unloads the package and moves it to the recovery area
  instead of permanently deleting it.

Installs stage outside the active extension directory. Updates require one
confirmation and preserve the replaced package in recovery. Device-local
locations are:

```text
%LOCALAPPDATA%\Hypershell\extensions
%LOCALAPPDATA%\Hypershell\extension-staging
%LOCALAPPDATA%\Hypershell\extension-recovery
```

`HYPERSHELL_APP_DATA` relocates all three for development and tests.

## Taxonomy

Choose the narrowest canonical `kind`:

- `Core`: language syntax, keywords, operators, literals, control flow, and
  language-defined built-ins that require no import;
- `StandardLibrary`: APIs distributed with a language/runtime;
- `Specification`: one named standards module, such as CSS Grid;
- `PlatformApi`: a platform surface such as DOM, Fetch, or a compiler API;
- `Framework`, `Module`, or `Package`: the exact ecosystem identity of
  third-party or separately imported content.

Do not place an imported API in Core. Do not copy one symbol into several
collections. Use cross-reference keywords or links while retaining one
canonical owner.

## Schema v2 descriptor

Schema v2 separates descriptors, searchable indexes, and detailed guides.
A descriptor contains identity and provenance plus either inline `commands` or
one or more `entryFiles`:

```json
{
  "schemaVersion": 2,
  "contentVersion": "1.0.0",
  "id": "python-httpx",
  "displayName": "HTTPX",
  "description": "HTTPX package APIs and guides.",
  "language": "Python",
  "kind": "Package",
  "canonicalName": "HTTPX",
  "ecosystem": "PyPI / Python",
  "targetVersion": "0.28",
  "sourceVersion": "HTTPX 0.28 API",
  "sourceUrl": "https://www.python-httpx.org/api/",
  "license": "BSD-3-Clause",
  "isExecutionSupported": true,
  "createStandaloneProfile": true,
  "executionTarget": "External",
  "declaredEntryCount": 2,
  "declaredIndexedCount": 1,
  "declaredGuidedCount": 1,
  "declaredVerifiedCount": 0,
  "entryFiles": ["entries/httpx.entries.json"],
  "declaredGuideCount": 1,
  "guideFiles": ["guides/httpx.guides.json"],
  "legacyIds": []
}
```

An entry shard supplies compact searchable metadata:

```json
{
  "schemaVersion": 2,
  "libraryId": "python-httpx",
  "entries": [
    {
      "id": "httpx.client.get",
      "name": "get",
      "displayName": "Client.get",
      "description": "Send a GET request through a Client.",
      "moduleName": "httpx",
      "commandType": "Reference index",
      "preferredShellKind": "Python",
      "documentationUrl": "https://www.python-httpx.org/api/#client",
      "symbolKind": "Method",
      "coverageTier": "Guided",
      "browsePath": [
        { "kind": "Package", "name": "httpx" },
        { "kind": "Type", "name": "Client" },
        { "kind": "Method", "name": "get" }
      ],
      "template": "client.get({url})",
      "compositionMode": "Statement",
      "guideFile": "guides/httpx.guides.json",
      "legacyIds": []
    }
  ]
}
```

The guide shard supplies the heavier explanation and example only when the
entry is opened:

```json
{
  "schemaVersion": 2,
  "libraryId": "python-httpx",
  "guides": [
    {
      "entryId": "httpx.client.get",
      "definition": "Client.get sends a GET request using the client's shared configuration and connection pool.",
      "parameters": [],
      "examples": [
        {
          "title": "Request a resource",
          "code": "response = client.get(\"https://example.com\")",
          "explanation": "The request reuses the Client's connection pool."
        }
      ]
    }
  ]
}
```

`Indexed` entries cannot point to a guide. `Guided` and `Verified` entries must
have usable explanation/example content, inline or through an exact guide
reference. Descriptor counts are checked against all shards.

## Migration and schema-v1 compatibility

Schema-v1 packages still validate and load. Use descriptor `legacyIds` when a
collection ID changes and entry `legacyIds` when an individual reference moves.
Aliases are migration keys, not duplicate content; package validation rejects
an alias that collides with another canonical ID or alias.

Saved library selections migrate before profile activation. Entry references
can resolve canonical IDs, original source IDs, or declared legacy aliases.

## Archive contract and limits

A `.hyperlib` file is a ZIP-compatible archive with
`hypershell.extension.json` at its root. The packer emits ordinal paths and a
fixed timestamp, so identical source content produces identical archive bytes.

Validation rejects:

- absolute paths, parent traversal, root escape, duplicate case-insensitive
  paths, symbolic links, and reparse points;
- more than 4,096 files or 256 MiB uncompressed content;
- a JSON content/shard file larger than 16 MiB;
- more than 10,000 entries in an external library or entry shard, 64 parameters
  per entry, or 10 examples per entry;
- invalid counts, duplicate IDs/aliases, orphaned guides, invalid provenance,
  or a non-`ContentOnly` package.

Large official catalogs may exceed the external-package library limit because
they are generated, reviewed application assets; they still use sub-16-MiB
lazy index shards.

## Host-owned execution routes

Static content cannot define a process launch. Executable entries must name a
language and terminal adapter registered by Hypershell:

- Python -> detected Python terminal;
- JavaScript -> detected Node.js terminal;
- TypeScript -> capability-gated Node.js type-stripping terminal;
- Java -> isolated detected JDK compiler/runtime;
- C++ -> isolated detected Clang, GCC, or MSVC C++20 toolchain;
- C# -> host-owned .NET runner where the declared surface is compatible.

HTML, CSS, and Web APIs are editor-guidance languages and must set
`isExecutionSupported: false`. Unknown languages remain useful as static guides
under the same rule.

## Start a package

```powershell
.\tools\New-HyperlibPackage.ps1 `
  -Id 'publisher.python-utilities' `
  -DisplayName 'Publisher Python Utilities' `
  -Publisher 'Publisher name' `
  -Language 'Python' `
  -Destination '.\work'
```

Keep review state `Draft` while content is unreviewed. Record authoritative
documentation, provenance, licensing, supported versions, and reviewers before
publishing.

Schemas and examples:

- `doc/schemas/hyperlib-command-library-v2.schema.json`
- `doc/schemas/hyperlib-command-entry-shard-v2.schema.json`
- `doc/schemas/hyperlib-command-guide-shard-v2.schema.json`
- `doc/schemas/hyperlib-command-library.schema.json` (v1)
- `samples/extensions/powershell-file-essentials` (v1 compatibility sample)

## Verification

```powershell
dotnet run --project tools\Hypershell.CSharpVerifier\Hypershell.CSharpVerifier.csproj `
  --configuration Release -- --hyperlib-packages-only

dotnet run --project tools\Hypershell.CSharpVerifier\Hypershell.CSharpVerifier.csproj `
  --configuration Release -- --official-language-packs-only
```

The lifecycle verifier covers schema v1, schema v2 inline/sharded loading,
descriptor-only discovery, deferred guides, deterministic packing, install,
update/rollback, traversal rejection, removal recovery, execution-route trust,
and migration aliases.
