# Internal support for the reviewed, signed-bundle GitHub publisher.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-HypershellGitHubReleasePlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $BundleDirectory,
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9-]{0,38}$')][string] $Owner,
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$')][string] $Repository,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string] $TargetCommit
    )

    $root = (Get-Item -LiteralPath $BundleDirectory).FullName
    if (-not (Test-Path -LiteralPath $root -PathType Container) -or
        (Get-Item -LiteralPath $root).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
        throw 'Choose an ordinary release bundle directory, not a symbolic link.'
    }
    $files = [Collections.Generic.List[object]]::new()
    $manifests = [Collections.Generic.List[object]]::new()
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($definition in @(
        @{ FileName = 'release-manifest.json'; Channel = 'stable'; Kinds = @('Installer', 'Portable') },
        @{ FileName = 'complete-manifest.json'; Channel = 'stable-complete'; Kinds = @('Installer') }
    )) {
        $path = Join-Path $root $definition.FileName
        $item = Get-Item -LiteralPath $path
        if ($item.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) -or $item.Length -gt 262144) {
            throw "The release manifest is not an ordinary bounded file: $($definition.FileName)"
        }
        $envelope = [IO.File]::ReadAllText($path) | ConvertFrom-Json -Depth 20
        $manifestBytes = [Convert]::FromBase64String($envelope.manifestBase64)
        if ($manifestBytes.Length -gt 131072) { throw 'The embedded manifest exceeds its size limit.' }
        $manifest = [Text.Encoding]::UTF8.GetString($manifestBytes) | ConvertFrom-Json -Depth 20
        if ($envelope.schemaVersion -ne 1 -or $manifest.schemaVersion -ne 1 -or
            $manifest.productId -ne 'com.atlasuniversalis.hypershell' -or
            $manifest.channel -ne $definition.Channel -or
            $manifest.architecture -ne 'win-x64' -or
            $manifest.version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$' -or
            $manifest.gitCommit -notmatch '^[0-9a-fA-F]{40}$') {
            throw "The $($definition.FileName) identity is not a coordinated stable Windows release."
        }
        $certificateBytes = [Convert]::FromBase64String($envelope.signingCertificateBase64)
        $certificateHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($certificateBytes)).ToLowerInvariant()
        $kinds = @($manifest.artifacts | ForEach-Object { [string] $_.kind } | Sort-Object)
        if (($kinds -join ',') -cne (($definition.Kinds | Sort-Object) -join ',')) {
            throw "The $($definition.FileName) artifact inventory is incomplete or unexpected."
        }
        [void] $names.Add($definition.FileName)
        $files.Add([pscustomobject]@{
            Name = $definition.FileName; Path = $path; Size = [long] $item.Length
            Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            RequiresAuthenticode = $false; Manifest = $true
        })
        foreach ($artifact in $manifest.artifacts) {
            $name = [string] $artifact.fileName
            if ($name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,199}$' -or -not $names.Add($name)) {
                throw 'The release contains an unsafe or duplicate artifact filename.'
            }
            $artifactPath = Join-Path $root $name
            $artifactFile = Get-Item -LiteralPath $artifactPath
            if ($artifactFile.PSIsContainer -or $artifactFile.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) -or
                $artifactFile.Length -le 0 -or $artifactFile.Length -ge 2GB -or
                $artifactFile.Length -ne $artifact.byteLength -or
                $artifact.sha256 -notmatch '^[0-9a-fA-F]{64}$') {
                throw "The release artifact has an invalid type or size: $name"
            }
            $hash = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($hash -cne ([string] $artifact.sha256).ToLowerInvariant()) {
                throw "The release artifact failed its manifest hash check: $name"
            }
            $expectedExtension = if ($definition.Channel -eq 'stable-complete') { '.exe' }
                elseif ($artifact.kind -eq 'Portable') { '.zip' } else { '.msi' }
            if ([IO.Path]::GetExtension($name) -cne $expectedExtension -or
                ($artifact.kind -eq 'Installer' -and $artifact.requiresAuthenticode -ne $true)) {
                throw "The release artifact does not match its component: $name"
            }
            $files.Add([pscustomobject]@{
                Name = $name; Path = $artifactPath; Size = [long] $artifactFile.Length; Sha256 = $hash
                RequiresAuthenticode = [bool] $artifact.requiresAuthenticode; Manifest = $false
            })
        }
        $manifests.Add([pscustomobject]@{ Name = $definition.FileName; Path = $path; Data = $manifest; SignerSha256 = $certificateHash })
    }
    $core = $manifests[0].Data
    $complete = $manifests[1].Data
    foreach ($property in @('version', 'gitCommit', 'publishedAtUtc', 'architecture', 'minimumWindowsVersion', 'releaseNotes')) {
        if ([string] $core.$property -cne [string] $complete.$property) {
            throw "The Core and Complete manifests disagree on $property."
        }
    }
    if ($manifests[0].SignerSha256 -cne $manifests[1].SignerSha256) {
        throw 'The Core and Complete manifests must use the same release signer.'
    }
    foreach ($entry in Get-ChildItem -LiteralPath $root -Force) {
        if ($entry.PSIsContainer -or -not $names.Contains($entry.Name)) {
            throw "The bundle contains an unlisted entry; only signed artifacts and the two manifests can be uploaded: $($entry.Name)"
        }
    }
    $body = @(
        "Hypershell Complete and Core $($core.version)", '', [string] $core.releaseNotes, '',
        "Source commit: $($core.gitCommit)", '',
        'The installers and portable archive are the same verified artifacts distributed through atlasuniversalis.com.', '',
        '| File | SHA-256 |', '| --- | --- |'
    )
    foreach ($file in $files) { $body += "| $($file.Name) | $($file.Sha256) |" }
    [pscustomobject]@{
        Owner = $Owner; Repository = $Repository; TargetCommit = $TargetCommit.ToLowerInvariant()
        Version = [string] $core.version; Tag = "v$($core.version)"; SourceCommit = [string] $core.gitCommit
        Name = "Hypershell Complete and Core $($core.version)"; Body = $body -join "`n"
        SignerSha256 = $manifests[0].SignerSha256; Manifests = $manifests.ToArray(); Files = $files.ToArray()
        SignatureVerified = $false
    }
}

function Assert-HypershellGitHubAsset {
    param([Parameter(Mandatory)] $Expected, [Parameter(Mandatory)] $Actual)
    if ($Actual.name -cne $Expected.Name -or $Actual.state -ne 'uploaded' -or
        [long] $Actual.size -ne $Expected.Size -or
        [string] $Actual.digest -cne "sha256:$($Expected.Sha256)") {
        throw "GitHub asset metadata does not match the verified local bytes: $($Expected.Name). Nothing was overwritten."
    }
}

function Invoke-HypershellGitHubDraft {
    [CmdletBinding()]
    param([Parameter(Mandatory)] $Plan, [Parameter(Mandatory)][scriptblock] $Request)
    if (-not $Plan.SignatureVerified) { throw 'Signature and Authenticode verification must complete before contacting GitHub.' }
    $base = "https://api.github.com/repos/$($Plan.Owner)/$($Plan.Repository)"
    $commit = & $Request 'GET' "$base/commits/$($Plan.TargetCommit)" $null $null
    if ([string] $commit.sha -cne $Plan.TargetCommit) { throw 'The GitHub destination does not contain the specified target commit.' }
    $release = $null
    for ($page = 1; $page -le 20; $page++) {
        $batch = @(& $Request 'GET' "$base/releases?per_page=100&page=$page" $null $null)
        $matches = @($batch | Where-Object { $_.tag_name -ceq $Plan.Tag })
        if ($matches.Count -gt 1) { throw 'Multiple releases claim the requested version tag.' }
        if ($matches.Count -eq 1) { $release = $matches[0]; break }
        if ($batch.Count -lt 100) { break }
        if ($page -eq 20) { throw 'The release inventory exceeded the bounded lookup; no draft was created.' }
    }
    if ($null -ne $release) {
        if ($release.draft -ne $true -or $release.prerelease -ne $false -or
            [string] $release.target_commitish -cne $Plan.TargetCommit -or
            [string] $release.name -cne $Plan.Name -or [string] $release.body -cne $Plan.Body) {
            throw 'An existing release differs from this exact draft plan or is already public. Nothing was overwritten.'
        }
    }
    else {
        # A tag can already exist without a release. Its resolved commit must agree.
        $tagReference = & $Request 'GET-OPTIONAL' "$base/git/ref/tags/$($Plan.Tag)" $null $null
        if ($null -ne $tagReference) {
            $tagCommit = & $Request 'GET' "$base/commits/$($Plan.Tag)" $null $null
            if ([string] $tagCommit.sha -cne $Plan.TargetCommit) {
                throw 'The existing GitHub tag points to a different commit. Nothing was overwritten.'
            }
        }
        $release = & $Request 'POST' "$base/releases" @{
            tag_name = $Plan.Tag; target_commitish = $Plan.TargetCommit; name = $Plan.Name
            body = $Plan.Body; draft = $true; prerelease = $false; generate_release_notes = $false
        } $null
    }
    if ([string] $release.id -notmatch '^[1-9][0-9]*$' -or $release.draft -ne $true) {
        throw 'GitHub did not return the expected draft identity.'
    }
    $releaseId = [string] $release.id
    $remoteFiles = @(& $Request 'GET' "$base/releases/$releaseId/assets?per_page=100" $null $null)
    $expectedNames = @($Plan.Files.Name)
    foreach ($remoteFile in $remoteFiles) {
        if ($remoteFile.name -cnotin $expectedNames) { throw 'The draft contains an unexpected asset. Nothing was overwritten.' }
    }
    foreach ($file in $Plan.Files) {
        $matches = @($remoteFiles | Where-Object { $_.name -ceq $file.Name })
        if ($matches.Count -gt 1) { throw 'The draft contains duplicate asset names.' }
        if ($matches.Count -eq 1) {
            Assert-HypershellGitHubAsset -Expected $file -Actual $matches[0]
            continue
        }
        # Construct the upload endpoint; never send a credential to a returned URL.
        $encodedName = [Uri]::EscapeDataString($file.Name)
        $upload = "https://uploads.github.com/repos/$($Plan.Owner)/$($Plan.Repository)/releases/$releaseId/assets?name=$encodedName"
        $uploaded = & $Request 'POST' $upload $null $file
        Assert-HypershellGitHubAsset -Expected $file -Actual $uploaded
    }
    # Read back the remote inventory independently after uploads, including retries.
    $finalRelease = & $Request 'GET' "$base/releases/$releaseId" $null $null
    if ($finalRelease.draft -ne $true -or [string] $finalRelease.body -cne $Plan.Body -or
        [string] $finalRelease.target_commitish -cne $Plan.TargetCommit -or
        [string] $finalRelease.tag_name -cne $Plan.Tag) {
        throw 'The release changed during upload. Review the GitHub release before proceeding.'
    }
    $finalAssets = @(& $Request 'GET' "$base/releases/$releaseId/assets?per_page=100" $null $null)
    if ($finalAssets.Count -ne $Plan.Files.Count) { throw 'The final GitHub asset inventory differs from the signed bundle.' }
    foreach ($file in $Plan.Files) {
        $match = @($finalAssets | Where-Object { $_.name -ceq $file.Name })
        if ($match.Count -ne 1) { throw 'A final GitHub artifact is missing or duplicated.' }
        Assert-HypershellGitHubAsset -Expected $file -Actual $match[0]
    }
    [pscustomobject]@{
        Draft = $true; ReleaseId = $releaseId; Version = $Plan.Version
        Url = "https://github.com/$($Plan.Owner)/$($Plan.Repository)/releases"
        ArtifactCount = $finalAssets.Count; VerifiedSha256 = $true
    }
}

Export-ModuleMember -Function Get-HypershellGitHubReleasePlan, Invoke-HypershellGitHubDraft
