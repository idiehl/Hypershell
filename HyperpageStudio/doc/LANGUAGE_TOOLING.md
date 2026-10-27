# Language tooling foundation

Status: process/discovery foundation, host-owned Python, Node.js, TypeScript,
and compiled Java command/document runners, plus editor-oriented HTML/CSS/Web
API guidance and local HTML preview implemented. Language Server Protocol transport, completion,
diagnostics, workspace configuration, and automatic server activation are
deliberately deferred.

## What the host owns

The current language-tooling layer is split into six responsibilities:

1. `LanguageToolRegistry` declares the tools and languages Hypershell knows how
   to integrate. These definitions are host code, not arbitrary user commands.
2. `LanguageToolDiscoveryService` performs a read-only PATH scan. It checks for
   files but does not execute version probes, download packages, contact a
   registry, or start a process.
3. `BoundedProcessRunner` launches one absolute native executable directly with
   `UseShellExecute=false` and `ArgumentList`. It never invokes `cmd.exe`,
   PowerShell, a terminal profile, or a shell-expanded command string.
4. `ExternalDocumentFormatterService` selects the highest-priority detected
   formatter for a document language and converts stdin to stdout. The editor
   applies returned text only after the process exits successfully and the host
   validates the result.
5. `LanguageServerProcessHost` owns process state and stdio streams for a future
   protocol client. Merely detecting a server never constructs or starts this
   host.
6. `CompiledLanguageToolchainRegistry` pairs host-approved compilers and
   runtimes, while `CompiledDocumentExecutionService` delegates source handling
   to a language adapter. Generated artifacts stay in a per-run temporary
   directory rather than the source tree.

Device detection is shown in **Settings > Language Tools**. Formatter and
language-server entries are capability diagnostics and do not authorize code
execution. Separately, terminal-profile discovery exposes installed Python and
Node.js runtimes and a complete Java JDK to the Command Builder through
host-owned adapters.

## Python, JavaScript, and TypeScript command execution

`CommandLanguageRegistry` is the shared policy seam between language packs,
profiles, the Command Lexicon, package validation, and terminal dispatch. It
normalizes language aliases, orders language containers, declares the allowed
execution target, and maps executable languages to terminal adapters. This
keeps a new content-only language data-driven while making executable support
an explicit host change.

Python statements run in one persistent interactive Python process per terminal
tab. JavaScript statements run in one persistent Node.js REPL per terminal tab
through a bounded host wrapper. TypeScript uses the same reusable Node REPL
transport but has its own profile, prompt, dispatch target, and source adapter.
Multiline statements, output, errors, working-directory updates, and session
variables remain associated with that tab. **Send to Terminal** stages source
in a compatible tab; **Run** executes it and creates a matching available
terminal tab when necessary.

Command Builder presentation and dispatch are language metadata rather than a
single command-centric layout. PowerShell, Python, JavaScript, and C# keep a
terminal-first primary action. TypeScript, Java, and C++ keep terminal actions
available but default composition delivery to a compatible source document.
HTML, CSS, and Web APIs expose only **Copy** and editor delivery, with vocabulary
such as elements, rules, and API snippets rather than commands. **Ctrl+Enter**
invokes the selected language's primary action. Editor delivery inserts at the
active compatible Monaco cursor, otherwise targets another compatible open
document, or creates an untitled document with the appropriate extension.

The TypeScript adapter is capability-gated rather than version-string-gated.
At profile discovery time, Hypershell directly invokes the detected Node.js
executable and verifies that `node:module.stripTypeScriptTypes` can erase a
typed statement. Node.js added this API in 22.13.0. Hypershell deliberately
uses its cross-version `strip` mode, not the removed-in-Node-26 `transform`
mode. As a result:

- annotations, interfaces, type aliases, unions, generics, `satisfies`, `as
  const`, and other erasable syntax can execute;
- syntax requiring JavaScript generation, such as TypeScript `enum` and
  namespaces, reports an explicit runner error without terminating the tab;
- execution does not perform semantic type-checking and is not represented as
  a replacement for a project build with `tsc`;
- no `npx` call, package download, global `ts-node`, or command-shell wrapper is
  required.

See the official Node.js
[`stripTypeScriptTypes` documentation](https://nodejs.org/api/module.html#modulestriptypescripttypescode-options)
for the capability contract and stability notes.

The application does not download either runtime. When Python or Node.js is
not installed or discoverable on `PATH`, the corresponding built-in terminal
profile remains visibly unavailable and execution reports the missing runtime.
When Node.js is present but lacks the native TypeScript API, JavaScript remains
available while the separate TypeScript profile is visibly unavailable.

## Java compiled execution

Java is the first consumer of the reusable compiled-language contract. A Java
route becomes available only when Hypershell detects both `javac` and `java`
from a JDK 17 or newer. `jshell` is reported separately as an optional detected
interactive shell for a future persistent-session adapter; the current Java
terminal deliberately compiles each submitted statement as a new program.

For a saved `.java` editor document, **Run document** snapshots the live Monaco
content, mirrors that snapshot as UTF-8 beneath a unique application temp root,
and resolves its package plus top-level class, record, or enum entry point. It
then invokes `javac -encoding UTF-8 --release 17 -proc:none -d <classes>
-sourcepath <roots> <source>` followed by `java -cp <classes> <entry-point>`.
The source folder is included for sibling source resolution, but `.class`
files are never emitted into the project. Compiler diagnostics, compiler
warnings, runtime diagnostics, timeouts, and successful output remain distinct
results, and the temporary source/class tree is reclaimed after every run.

This follows the JDK command contracts for
[`javac`](https://docs.oracle.com/en/java/javase/25/docs/specs/man/javac.html),
[`java`](https://docs.oracle.com/en/java/javase/25/docs/specs/man/java.html),
and the optional
[`jshell`](https://docs.oracle.com/en/java/javase/25/docs/specs/man/jshell.html).
Hypershell does not download or modify a JDK.

## HTML, CSS, and browser API workflow

HTML and CSS are document languages, and browser APIs require a page runtime;
Hypershell therefore does not present any of them as terminal commands. Their
official starter packs publish first-class Lexicon tabs and compose patterns
through an editor-first action. HTML is organized around elements and document
blocks; CSS is organized around selectors, rules, at-rules, and canonical
properties. The host allows HTML patterns in HTML documents, CSS patterns in
CSS or SCSS documents, and Web API patterns in HTML, JavaScript, or TypeScript
documents. When markup must be inserted without a live cursor, the host places
it before a closing `body` or `html` element instead of appending invalid text
after the document.

The new-document dialog supplies linked-page and self-contained HTML templates,
a responsive CSS foundation, and browser-module starters for JavaScript and
TypeScript. **Preview HTML in browser** saves an unsaved page when necessary,
then opens or refreshes a local-file preview in a Hypershell browser tab. The
saved location provides the base URI for relative stylesheets, modules, images,
and other assets. TypeScript remains source code: it must be compiled to
JavaScript before a browser page imports it, and the direct preview workflow
does not claim otherwise.

## Formatter contract

External formatting occurs only after the user chooses **Format** or its
keyboard shortcut. The current process policy is:

- input is the current in-memory document, not a temporary project file;
- input and accepted output are each capped at 8 MiB;
- stderr is capped at 64 KiB;
- execution is capped at 10 seconds;
- timeout, cancellation, or an output-limit violation terminates the process
  tree;
- nonzero exit, invalid output, NUL content, or unexpectedly empty output leaves
  the document untouched;
- unavailable, rejected, failed, and timed-out adapters route to Monaco's local
  formatter or safe whitespace fallback;
- successful output is reapplied through the document model so explicit or
  mixed line-ending policy remains intact.

Some formatter CLIs need a filename to select a parser or discover nearby
configuration. Hypershell may supply the real document path, or an unsaved
metadata path, only through the formatter's documented filename option. It
does not request in-place editing. This is a process boundary, not an OS
sandbox: an installed executable still runs with the user's Windows account
permissions and can access resources that account can access.

The initial external adapter set is:

| Language | Preferred adapter | Stdin/stdout invocation |
| --- | --- | --- |
| Python | Ruff, then Black | `ruff format --stdin-filename <path> -`; `black --quiet --stdin-filename <path> -` |
| Go | gofmt | no file argument |
| Rust | rustfmt | no file argument |
| C / C++ | clang-format | `--assume-filename=<path>` |
| Shell | shfmt | no file argument |

The CLI modes follow the upstream documentation for
[Ruff](https://docs.astral.sh/ruff/formatter/),
[Black](https://black.readthedocs.io/en/latest/usage_and_configuration/the_basics.html),
[gofmt](https://pkg.go.dev/cmd/gofmt),
[rustfmt](https://github.com/rust-lang/rustfmt),
[clang-format](https://clang.llvm.org/docs/ClangFormat.html), and
[shfmt](https://github.com/mvdan/sh).

Monaco remains the built-in fallback and covers every editor language. A tool
being listed does not imply it is installed; the Settings snapshot reports its
device-specific state.

## Executable resolution and command wrappers

On Windows, discovery accepts `.exe` and `.com` files as direct native launch
targets. A matching `.cmd` or `.bat` file is shown as **WRAPPER IGNORED**. This
is common for npm-installed tools. Hypershell does not silently pass those
wrappers to a command shell because doing so would reintroduce command parsing
and a much broader execution surface.

A later package-specific launcher may support trusted wrappers by resolving a
known runtime and a validated script entry point into a direct `ArgumentList`.
It should not add a generic "run this command" formatter contract.

## Language-server lifecycle

The process host starts in `Stopped` and transitions through:

```text
Stopped -> Starting -> Running -> Stopping -> Stopped
                         |
                         +-----------------> Faulted
```

Starting requires an already discovered native language-server definition and
an explicit call from a future language client. The host exposes raw input and
output streams, drains bounded diagnostic text, closes stdin during graceful
shutdown, and kills the process tree if shutdown does not finish promptly.

Not implemented in this slice:

- JSON-RPC framing or LSP initialization;
- workspace-folder and document synchronization;
- completion, hover, symbols, diagnostics, rename, or code actions;
- server configuration, per-project consent, restart/backoff, or crash UX;
- npm command-wrapper launch;
- extension-contributed executable definitions.

Those features must sit above the process host so discovery, trust, lifecycle,
and UI policy stay reusable across languages.

## Verification

`Hypershell.CSharpVerifier --language-tooling-only` exercises:

- built-in, native, ignored-wrapper, and missing discovery states;
- registry ID and language-reference integrity;
- formatter applied and unchanged results;
- nonzero exit with a non-destructive fallback result;
- timeout and process-tree termination;
- output and input safety limits;
- stopped-by-default language-server start/stream/stop lifecycle.

The full verifier runs the same checks under the `language-tooling` stage.
`--official-language-packs-only` additionally loads the bundled Python,
JavaScript, TypeScript, Java, HTML, CSS, and Web API packs; validates their
guide/template metadata and Lexicon containers; checks compatible editor
targets, browser-JavaScript syntax, starter-document content, and saved local
preview addressing; syntax-checks every executable rendered template and
example; runs all 15 safe TypeScript examples; discovers the interpreted and
compiled terminal routes; executes real statements where the device toolchain
is available; confirms that interpreted state persists; and verifies clean
recovery after transform-required TypeScript syntax is rejected. A synthetic
JDK pair always verifies Java compile/run dispatch, diagnostics, document and
snippet entry points, source-tree isolation, and temporary-output cleanup.
