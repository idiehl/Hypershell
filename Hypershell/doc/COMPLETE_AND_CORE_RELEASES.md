# Hypershell Complete and Core

Complete is the recommended Windows x64 download. Core contains Hypershell and
MyNet. Complete installs that same Core MSI plus optional Hyperlib/Hyperpage
Studio and Atlas Meridian payloads. The initial Complete screen selects both
optional components by default. Shared application code remains on a coordinated
release; this is not an independently updating plugin marketplace.

The configured destinations are in `distribution/targets.json`:

- Product page: `https://atlasuniversalis.com/Downloads` (Atlas.Web repository).
- Download service: `https://hypershell.atlasuniversalis.com/downloads`.
- GitHub destination: `idiehl/hypershell-releases`, a downloads-only repository.

These are configuration targets, not evidence of deployment or publication.
The GitHub publisher requires the destination repository to exist. It never
pushes the private source repository, creates a repository, or publishes a draft.

## Release artifacts and compatibility

A Complete release directory contains exactly five files:

| File | Purpose |
| --- | --- |
| `Hypershell-VERSION-win-x64.msi` | Core installer; legacy filename retained for existing consumers |
| `Hypershell-VERSION-win-x64-portable.zip` | Core recovery/portable archive |
| `release-manifest.json` | Signed existing-schema Core manifest, channel `stable` |
| `Hypershell-Complete-VERSION-win-x64.exe` | Complete offline bundle |
| `complete-manifest.json` | Signed existing-schema Complete manifest, channel `stable-complete` |

The manifests share the version, source commit, publication time, compatibility,
release notes, and signer. Complete uses one EXE Installer artifact. The existing
private stable API continues returning the original Core manifest so older
clients do not receive an unknown artifact kind or schema. Complete installations
direct users to the Complete download instead of handing a Core MSI to the
legacy updater; stale Core download/install candidates are rejected too.

The public catalog lists only releases imported with their matching Complete
companion. Existing private releases do not become public. A previously imported
version cannot gain, lose, or replace its companion; create a new version.

## Prerequisites

Follow `doc/RELEASES.md` to establish the trusted Atlas code-signing certificate,
desktop signer pin, and server signer configuration. Production release tools
refuse dirty sources or untrusted signing certificates. Never substitute a test
certificate for public distribution.

Complete also requires local copies of:

- The **offline Microsoft WebView2 x64 runtime installer**, named
  `MicrosoftEdgeWebView2RuntimeInstallerX64.exe`, not the online bootstrapper.
- The **.NET 10 SDK x64 installer** for the exact version passed to the publisher.
  Studio compiles work designs; a self-contained .NET application runtime does
  not satisfy this prerequisite.
- The independently recorded SHA-256 of each downloaded prerequisite.

The publisher checks both hashes, valid Microsoft Authenticode signatures, SDK
product/version/architecture metadata, and WebView2 standalone identity. The
Complete bundle installs WebView2 when absent and the SDK when Authoring is
selected and a suitable SDK is absent. Shared Microsoft prerequisites remain
installed when Complete is removed. Core's standalone MSI checks for WebView2
and explains how to install it if missing; Complete handles that prerequisite
automatically. Test both machine and per-user WebView2 cases in Windows QA.

## Prepare clean component payloads

Studio is currently built from the authoring source checkout, while Meridian
has its own repository. First integrate and commit the intended source in each
repository. The Studio source must include the Complete updater guard from
`ApplicationUpdateService.cs` and the per-user Complete projects location in
`VisualPageProjectService.cs`. Do not use published outputs from live development
sessions as release input.

```powershell
./tools/New-HypershellSuitePayload.ps1 `
  -Version 1.2.3 `
  -StudioSourceRoot C:/path/to/committed/Hypershell `
  -MeridianSourceRoot C:/path/to/committed/Meridian `
  -OutputDirectory C:/release-work/1.2.3-components
```

This command exports the exact clean commits into isolated build snapshots,
builds the native Studio, Hyperlib host and Meridian host, and creates `payload/`
with a version/provenance receipt and a complete file hash inventory. Node/npm
is needed on the build workstation for Meridian; it is not an end-user prerequisite.
The payload validator and installer harvester reject missing files, altered bytes,
unsafe paths, links, and private development state. The publisher verifies its
own scratch copy again before signing it.

The Hyperlib seed is explicitly curated from the Core/Runtime source SDK and
`Starters/standard`. A real isolated startup/render smoke test is part of the
Complete build gate. It does not
copy the development library's authored Works, content journal, media collection,
accounts, or registrations. On first use, MyNet discovers the installed apps
and initializes a writable library in the user's profile. Existing registrations
win, the ordinary command trust review remains in force, and upgrades do not
overlay an already initialized user library.
New Studio projects in a Complete installation default to the user's Hypershell
profile Projects directory, so creating a project never requires write access
to Program Files. Existing standalone installations retain their prior default.

## Build both editions

```powershell
./tools/Publish-HypershellRelease.ps1 `
  -Version 1.2.3 -Edition Complete `
  -ReleaseNotesFile C:/release-work/notes-1.2.3.md `
  -CertificateThumbprint ATLAS_CERTIFICATE_THUMBPRINT `
  -SuitePayloadDirectory C:/release-work/1.2.3-components/payload `
  -WebView2InstallerPath C:/release-work/prerequisites/MicrosoftEdgeWebView2RuntimeInstallerX64.exe `
  -WebView2InstallerSha256 WEBVIEW2_SHA256 `
  -DotNetSdkInstallerPath C:/release-work/prerequisites/dotnet-sdk-10.0.401-win-x64.exe `
  -DotNetSdkInstallerSha256 SDK_SHA256 `
  -DotNetSdkVersion 10.0.401
```

The version above is an example, not a scheduled or published release. The
default `-Edition Core` preserves the existing Core-only build workflow.
Complete reuses the tested Core MSI, signs the component binaries and MSIs,
builds the Burn bundle, signs its detached engine, reattaches it, signs the final
EXE, and verifies both manifests. Outputs are immutable and incomplete output is
kept for diagnosis, never reported as successful. Source and signing receipts
belong outside the final five-file download directory.

Burn engine signing follows the maintained
[WiX signing procedure](https://docs.firegiant.com/wix/tools/signing/).

## Website publication

Deploy the updated Hypershell profile server (including metadata schema 4 and
the paired-release importer) and the updated Atlas.Web download page using
their established deployment procedures. Existing live services are not
modified by local build/test commands.

Promote the updated `deploy/profile-server/nginx-https.conf` too: it explicitly
allows `/downloads`, its catalog/artifact routes, and the public page assets on
the Hypershell hostname while retaining the default 404 for other website paths.

`Publish-HypershellStableRelease.ps1` accepts the same Complete parameters and
performs its existing source/signing/server preflight, build, exact hash-checked
upload and server import. Its Core-only mode remains supported. The server
publisher accepts either the three Core files or all five coordinated files;
partial companions and extra files are rejected. Its staging bound is 2 GiB
per artifact and 6 GiB in aggregate.

Public routes:

```text
/downloads
/downloads/releases
/downloads/VERSION/core/installer
/downloads/VERSION/core/portable
/downloads/VERSION/core/manifest
/downloads/VERSION/complete/installer
/downloads/VERSION/complete/manifest
```

The Atlas page reads the public catalog with a bounded request to the configured
fixed HTTPS service. It shows download buttons only for a valid published pair.
An empty catalog shows an unpublished state; service failure shows unavailable.
The private admin downloads page and private release API retain their access rules.

## GitHub draft

Use a public downloads-only repository under `idiehl`. Its initial commit can
contain just product information and a link to the website. `TargetCommit` is
the commit in that destination repository, not the private application source
commit. Both manifests retain the actual source identity.

```powershell
# Offline inventory/hash plan; does not assert trusted signature verification.
./tools/Publish-HypershellGitHubRelease.ps1 `
  -BundleDirectory ./release/1.2.3 `
  -Owner idiehl -Repository hypershell-releases `
  -TargetCommit DESTINATION_COMMIT_SHA `
  -TrustedSignerSha256 ATLAS_CERTIFICATE_SHA256 -WhatIf
```

For upload, supply a token through `GH_TOKEN` with Contents write access to that
repository and omit `-WhatIf`. The command validates both trusted signatures
and installer Authenticode before contacting GitHub, creates a draft, uploads
the exact five files, and checks remote SHA-256 digests. Retries resume only the
same draft; conflicting or public releases are never overwritten. Inspect the
draft and complete release QA before making it public. Then update the Atlas
GitHub link from the profile to the verified releases repository.

## Validation and remaining release gates

The checked-in Windows CI workflow builds release tooling and runs paired
distribution, updater, discovery, payload, GitHub retry, and installer fixture
tests. The installer fixture compiles three MSIs and the Complete EXE using inert
files; it does not install them and cannot be distributed.

```powershell
dotnet run --project tools/Hypershell.DistributionVerifier -c Release
dotnet run --project tools/Hypershell.SuiteVerifier -c Release
./tools/Test-HypershellSuitePayloadValidation.ps1
./tools/Test-HypershellGitHubRelease.ps1
./installer/Suite/Test-SuiteInstaller.ps1 -Build
```

Before the first public release, use a disposable clean Windows environment to
exercise actual trusted Core/Complete installation, prerequisite detection,
both component choices, launch/render of all apps, upgrade/repair/uninstall,
per-user data preservation, and download verification from both destinations.
Bundle maintenance currently offers repair/uninstall; changing optional
components after installation is not implemented in the first setup UI.

The first setup cycle does not provision an Atlas signing certificate, commit
unrelated development changes, create the GitHub repository, deploy either
website, or publish a release. Those require the real release inputs and their
successful end-to-end validation.
