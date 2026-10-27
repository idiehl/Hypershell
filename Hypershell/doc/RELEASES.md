# Hypershell signed releases

For the coordinated **Complete and Core** installer setup, component staging,
public Atlas downloads, and GitHub draft publication, see
[COMPLETE_AND_CORE_RELEASES.md](COMPLETE_AND_CORE_RELEASES.md). The existing
Core manifest and private updater contract described below remain supported.

Hypershell stable releases use two independent checks:

1. A compact release manifest is signed by the Atlas code-signing certificate.
2. Windows Authenticode validates the MSI and confirms that its signer is the
   same certificate named by that manifest.

The manifest also fixes the version, channel, Git commit, publication time,
minimum Windows version, file names, byte lengths, and SHA-256 hashes. The
account service accepts only configured signer fingerprints and stores release
artifacts by content hash. Desktop clients accept only signer fingerprints
bundled in `Assets/Release/trusted-signers.json`.

## Prerequisites

- A trusted code-signing certificate with its private key installed in the
  current Windows user's certificate store.
- The certificate must allow digital signatures and carry the Code Signing EKU
  (`1.3.6.1.5.5.7.3.3`).
- Windows SDK SignTool must be installed.
- .NET 10 and Git must be available.
- The intended source must be committed. Production builds refuse a dirty
  worktree by default.
- A UTF-8 release-notes file no larger than 32 KiB.

The private key, PFX, password, and timestamp credentials must never enter the
repository, server environment, profile data, logs, or a release bundle.

## Establish trust before the first release

Validate the installed certificate chain, private key, EKU, and key usage, then
atomically add its SHA-256 fingerprint to the desktop trust policy:

```powershell
.\tools\Set-HypershellReleaseSigner.ps1 `
  -CertificateThumbprint CERTIFICATE_THUMBPRINT
```

The command prints the lowercase SHA-256 value used by both trust boundaries:

- Desktop build: `src/Hypershell/Assets/Release/trusted-signers.json`
- Server deployment: `HYPERSHELL_RELEASE_SIGNER_SHA256`

The desktop trust document intentionally contains no signer until the real
certificate is acquired. An empty document disables update checks without
making a network request.

Use `-CheckOnly` to validate a certificate without changing the trust policy.
After committing and pushing the desktop pin, create a fresh server backup and
promote the running image with
`--release-signer-sha256 SHA256_FINGERPRINT`. The promotion records both old
and new public fingerprints in its rollback evidence; the private key never
leaves the release workstation.

When rotating certificates, ship a transition build that trusts both old and
new signer fingerprints, publish it with the old certificate, then use the new
certificate only after supported installations have received the transition.

## Build a stable release

From the repository root:

```powershell
.\tools\Publish-HypershellRelease.ps1 `
  -Version 1.2.3 `
  -ReleaseNotesFile .\release-notes-1.2.3.md `
  -CertificateThumbprint CERTIFICATE_THUMBPRINT
```

The script:

- runs the full Hypershell verifier;
- publishes a self-contained Windows x64 Release build;
- Authenticode-signs Hypershell executables and managed application binaries;
- builds a per-machine WiX MSI with upgrade and Start Menu support;
- Authenticode-signs the MSI using an RFC 3161 timestamp;
- produces a portable recovery ZIP;
- signs the release manifest;
- verifies its chain, signer pin, sizes, and hashes;
- preserves the completed bundle under `release/<version>`.

The script never overwrites an existing release directory. `-AllowDirty` is for
diagnostic builds only. `-SkipVerification` is not acceptable for stable
publication.

## Import and serve

Once the signer pin is committed, pushed, and activated by a blue-green server
promotion, the release workstation can perform the full build, hash-compared
upload, and immutable server import in one command:

```powershell
.\tools\Publish-HypershellStableRelease.ps1 `
  -Version 1.2.3 `
  -ReleaseNotesFile .\release-notes-1.2.3.md `
  -CertificateThumbprint CERTIFICATE_THUMBPRINT
```

This command refuses a dirty or unpushed commit, an unpinned certificate, a
missing block volume, a mismatched production signer, or unaudited server
publisher tooling. Upload occurs through a temporary root-owned directory and
every file is hash-compared before the directory is atomically exposed to the
server publisher. Both the workstation and server re-check the Git commit, and
the server rejects the signed bundle unless its embedded commit exactly matches
the private repository's current `main`. Use `-Pin` only for a release that must
bypass normal retention.

For an audited manual retry after a completed upload, invoke the installed
server publisher against the exact root-owned incoming directory:

```bash
/opt/hypershell/profile-server/deploy/hypershell-server/publish-profile-release.sh \
  --bundle /srv/hypershell/releases/incoming/1.2.3 \
  --signer-sha256 64_HEX_CHARACTER_CERTIFICATE_SHA256
```

Use `--pin` for a rollback or support release that must survive normal
retention. Verify inventory through `release list`, sign into the admin portal,
and download both artifacts once before announcing the release. The server
keeps archive directories only for versions still present in that strict
latest-three-plus-pinned inventory; publication evidence records each archive
pruned during reconciliation.

If upload fails before the atomic incoming handoff, the workstation attempts to
remove its uniquely named root-owned staging directory. If server publication
fails after that handoff, the incoming version directory is deliberately kept
for diagnosis and an audited retry.

## Desktop update behavior

Signed-in clients may check the private stable channel from Settings. Checking
does not download or install anything. Download streams to a temporary file,
enforces the signed length and SHA-256, verifies Authenticode and the signer
identity, then atomically places the MSI under:

```text
%LOCALAPPDATA%\Hypershell\updates\<version>\
```

Install requires an explicit confirmation. Hypershell starts a hidden handoff,
closes, waits for the current process, requests Windows elevation for MSI
installation, and reopens the installed application on success. The current
and two prior staged installer directories are retained for local rollback.

## Failure and recovery rules

- Redirects, non-JSON manifests, oversized payloads, unknown signers, invalid
  chains, wrong EKUs, expired certificates, bad signatures, hash mismatches,
  unexpected lengths, and Authenticode mismatches are hard failures.
- A rejected or interrupted download leaves neither a destination installer nor
  a partial temporary file.
- Release versions are immutable on the server.
- The latest three unpinned stable releases and every pinned release remain
  downloadable.
- The updater never forces installation and never runs an unverified staged
  file.

Before publishing, run:

```powershell
dotnet run --project .\tools\Hypershell.CSharpVerifier\Hypershell.CSharpVerifier.csproj -c Release
```

The release verifier covers signer pinning and usage, content-addressed server
storage, retention, authenticated APIs, strict HTTP handling, atomic desktop
downloads, tamper replacement, reuse, and unsigned-installer rejection.

Before the trusted certificate is available, exercise the exact local
packaging path with:

```powershell
.\tools\Test-HypershellReleasePipeline.ps1
```

This diagnostic creates a one-day self-signed Code Signing certificate and
builds the real MSI, portable ZIP, and signed manifest without adding a trust
root to Windows. The release script accepts that untrusted signer only for the
`diagnostic` channel, only beneath `output\release-pipeline-test`, and only with
an explicitly dirty non-production build. The test checks MSI metadata and the
portable binary signatures, then removes the temporary certificate and
artifacts. Its output is never eligible for server publication and does not
replace the trusted-certificate requirement for a stable release.
