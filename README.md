# Hypershell source snapshot

Current development source for Hypershell, Hyperpage Studio / Hyperlib, and Atlas Meridian, saved together so development can continue on another computer. This is a work-in-progress source snapshot, not a finished release or an installer.

## Folders

- `Hypershell/`: the current main Hypershell working tree, including Hyperlib source, reusable templates, and the in-progress distribution tools.
- `HyperpageStudio/`: the separate Studio authoring working tree. It contains its own copy of the Hypershell host because Studio development currently lives in a different checkout. Both versions are preserved so the current Studio changes remain available alongside the main shell.
- `Meridian/`: Atlas Meridian source, integration tools, and example Works.

`SOURCE_SNAPSHOT.json` identifies the source baselines and records the exclusions. `SOURCE_FILES.sha256` lists the SHA-256 of every component source file as stored in Git and the downloadable ZIP. Git applies the included text line-ending rules; a checkout may use different line endings on your operating system.

## Build and run on Windows

Install the .NET 10 SDK, Node.js 24 with npm, and Microsoft Edge WebView2 Runtime. Run these commands from the snapshot root in PowerShell. NuGet and npm restore dependencies from their configured public feeds.

Hypershell:

```powershell
dotnet run --project Hypershell/src/Hypershell/Hypershell.csproj
```

Hyperpage Studio:

```powershell
dotnet run --project HyperpageStudio/src/Hypershell/Hypershell.csproj -- --hyperpage-studio
```

Hyperlib (creates a fresh local library from the included starter):

```powershell
./Hypershell/Hyperlib/Start-HyperLib.ps1 -OpenBrowser
```

Meridian:

```powershell
Set-Location Meridian
npm ci
npm run build
npm start
```

Then open `http://127.0.0.1:5240`. The main Meridian host uses `%LOCALAPPDATA%/AtlasMeridian` for its saved Works. Its MCP integration has a separate `integrations/mcp/package.json` and setup guide.

Each component's existing documentation contains additional development and verification commands. Some older helper scripts and documentation still refer to the original development machine's absolute paths; use the relative commands above to start on another machine.

## Snapshot scope

This snapshot includes current source edits, assets, reusable templates, tests, documentation, and in-progress installer/release code. Downloading or building this source does not require a signing certificate.

It excludes Git internals, dependency caches, build outputs, generated QA captures, local secrets/certificates, runtime locks/journals/recovery stores, saved Hyperlib Works/library content, the main checkout's authored `pages/`, and Meridian's downloaded research corpus in `content/`. Stock Hyperlib media and reusable template assets are included. Saved personal application profiles outside the source trees are not included. The exclusion rules are recorded in `SOURCE_SNAPSHOT.json`.

The `Hypershell/` and `HyperpageStudio/` trees are independent development baselines. This snapshot is not a claim that they have been integrated or that all features are production-ready.
