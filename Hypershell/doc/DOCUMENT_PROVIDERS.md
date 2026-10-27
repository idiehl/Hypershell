# Typed document providers and export

Status: built-in text, Markdown, and JSON providers are active. Standalone HTML
and Microsoft Word (`.docx`) export are implemented. This is an export layer,
not an Office editor or a binary-document importer.

## Why this layer exists

The editor still stores source as text, but callers no longer need to assume
that every document has identical semantics. A bound provider owns four typed
operations:

- `read`: return a summary, bounded outline, and diagnostics;
- `replace`: replace a validated source range;
- `insert`: insert at a validated source offset;
- `export`: create a separate rendered artifact without changing the source.

`DocumentTabViewModel` routes these operations through the provider selected by
`DocumentProviderRegistry`. This is also the seam a future AI permission broker
can use for document-aware edits instead of granting direct, unstructured UI
access.

## Providers available now

| Provider | Source modes | Read behavior | Mutation behavior |
| --- | --- | --- | --- |
| Code and text | Every editor language without a more specific provider | Line and character summary | Range-checked text edits |
| Markdown | `markdown`, `.md`, `.markdown`, and safe aliases | Block count and heading outline | Range-checked source edits |
| Structured JSON | `json`, `.json`, `.jsonc`, and safe aliases | Parse diagnostics and an outline capped at 2,048 items | All-or-nothing edits; invalid JSON is rejected and the original source is retained |

Provider selection first considers an active file-extension registration, then
the document language, and finally the built-in text fallback. A provider is
rebound after Open, Save As, session restore, and extension refresh. The bound
provider and its source are visible in the editor status bar, and its active
extensions participate in the Open and Save As filters.

## Export behavior

The editor toolbar and document-tab menu expose **Export** when the bound
provider declares at least one format. Export always creates a copy. It never
marks the source as saved, changes the source path, or overwrites the active
source document.

### Standalone HTML

- UTF-8 without a byte-order mark;
- one self-contained document with static inline styling;
- a restrictive content-security policy with scripts, network connections,
  objects, frames, media, forms, and external base URLs disabled;
- raw Markdown HTML disabled;
- non-HTTP(S)/`mailto` links neutralized;
- Markdown images replaced with labeled placeholders so export does not create
  a hidden external fetch;
- text, code, and JSON escaped inside preformatted content.

### Microsoft Word

Hypershell creates a real Open XML `.docx` package with document metadata,
paragraph and heading styles, list numbering, code and quote styles, and page
settings. Markdown headings, lists, quotes, rules, code blocks, and prose become
corresponding Word structures. The host runs `OpenXmlValidator` before an
artifact can leave the provider; an invalid package is rejected.

### Host limits and write boundary

- source input is capped at 8,388,608 in-memory characters;
- an artifact is capped at 64 MiB;
- a provider must return the exact format the host requested;
- the selected filename extension must match that format;
- output is written to a temporary sibling and atomically moved into place;
- temporary siblings are removed after success, failure, or cancellation;
- if source text changes while an artifact is being created, the stale artifact
  is discarded.

## Static extension mappings

A `ContentOnly` extension may introduce a file-extension alias without shipping
or executing a document provider. It requests `DocumentProvider` and contributes
`TextEditor` metadata that selects a host-owned language/provider:

```json
{
  "schemaVersion": 1,
  "id": "community.markdown-notes-alias",
  "displayName": "Markdown Notes Alias",
  "publisher": "Example Publisher",
  "version": "1.0.0",
  "runtime": "ContentOnly",
  "requestedCapabilities": ["DocumentProvider"],
  "contributions": {
    "documentFormats": [
      {
        "id": "markdownNotes",
        "displayName": "Markdown Notes",
        "kind": "TextEditor",
        "fileExtensions": [".hnote"],
        "languageId": "markdown"
      }
    ]
  }
}
```

Activation is deliberately conservative:

1. The whole package must pass normal manifest and package validation.
2. Only validated `ContentOnly` + `TextEditor` mappings are considered.
3. A core-owned extension remains bound to the core; the package mapping is
   shown as dormant.
4. If multiple packages claim the same new extension, every conflicting
   mapping remains dormant.
5. An unrecognized language ID receives the host text provider. No extension
   assembly, script, handler, WebView, terminal, network, or AI capability is
   loaded.

The copyable sample is under
`samples/extensions/markdown-notes-alias`. Settings > Extensions shows each
mapping, its active/dormant state, and the reason for a conflict.

## Contract locations

- `src/Hypershell/Models/DocumentProviderModels.cs`
- `src/Hypershell/Services/BuiltInDocumentProviders.cs`
- `src/Hypershell/Services/DocumentProviderRegistry.cs`
- `src/Hypershell/Services/DocumentRenderingService.cs`
- `src/Hypershell/Services/DocumentExportService.cs`
- `src/Hypershell/Extensibility/ExtensionContracts.cs`

Run the focused verification with:

```powershell
dotnet run --project tools\Hypershell.CSharpVerifier\Hypershell.CSharpVerifier.csproj -c Release -- --document-providers-only
```

The verifier covers typed reads and mutations, JSON rollback, HTML
sanitization, Open XML package validation, atomic writes, size and extension
limits, core ownership, duplicate claims, safe alias activation, and tab-level
provider routing.

## Deferred work

- PDF export and print-preview integration;
- opening or editing `.docx`, PDF, spreadsheet, presentation, and other binary
  formats;
- executable `WebViewer`, `NativeViewer`, or third-party provider activation;
- rich visual Markdown preview inside the workspace;
- AI permission-broker routing and audit records;
- collaboration, tracked changes, comments, and revision synchronization.
