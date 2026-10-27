#Requires -Version 7.4
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'GitHubRelease.psm1') -Force
$testParent = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../output/github-release-tests'))
$testRoot = Join-Path $testParent ([Guid]::NewGuid().ToString('N'))
[void] [IO.Directory]::CreateDirectory($testRoot)
$checks = 0
function Require([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}
function Reject([scriptblock] $Action, [string] $Pattern) {
    try { & $Action | Out-Null }
    catch {
        Require ($_.Exception.Message -like "*$Pattern*") "Unexpected failure: $($_.Exception.Message)"
        return
    }
    throw "Expected rejection: $Pattern"
}
function Write-Manifest([string] $Path, $Data, [byte[]] $Certificate = [byte[]](1, 2, 3, 4)) {
    $manifestBytes = [Text.Encoding]::UTF8.GetBytes(($Data | ConvertTo-Json -Depth 10 -Compress))
    @{
        schemaVersion = 1; manifestBase64 = [Convert]::ToBase64String($manifestBytes)
        signingCertificateBase64 = [Convert]::ToBase64String($Certificate)
        signatureAlgorithm = 'RSA-PKCS1-SHA256'; signatureBase64 = 'AA=='
    } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $Path -Encoding utf8NoBOM
}
function New-Fixture {
    $bundle = Join-Path $testRoot ([Guid]::NewGuid().ToString('N'))
    [void] [IO.Directory]::CreateDirectory($bundle)
    $artifacts = foreach ($item in @(
        @{ Name = 'Hypershell-Core-1.2.3-win-x64.msi'; Kind = 'Installer' },
        @{ Name = 'Hypershell-Core-1.2.3-win-x64-portable.zip'; Kind = 'Portable' },
        @{ Name = 'Hypershell-Complete-1.2.3-win-x64.exe'; Kind = 'Installer' }
    )) {
        $path = Join-Path $bundle $item.Name
        [IO.File]::WriteAllText($path, "fixture:$($item.Name)")
        [ordered]@{
            kind = $item.Kind; fileName = $item.Name; byteLength = (Get-Item -LiteralPath $path).Length
            sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            requiresAuthenticode = $item.Kind -eq 'Installer'
        }
    }
    $common = @{
        schemaVersion = 1; productId = 'com.atlasuniversalis.hypershell'; version = '1.2.3'
        architecture = 'win-x64'; gitCommit = 'a' * 40; publishedAtUtc = '2026-10-26T12:00:00.0000000+00:00'
        minimumWindowsVersion = '10.0.17763'; releaseNotes = 'Test release notes.'
    }
    $core = $common.Clone(); $core.channel = 'stable'; $core.artifacts = @($artifacts[0], $artifacts[1])
    $complete = $common.Clone(); $complete.channel = 'stable-complete'; $complete.artifacts = @($artifacts[2])
    Write-Manifest (Join-Path $bundle 'release-manifest.json') $core
    Write-Manifest (Join-Path $bundle 'complete-manifest.json') $complete
    [pscustomobject]@{ Root = $bundle; Core = $core; Complete = $complete }
}
function Get-Plan($Fixture) {
    Get-HypershellGitHubReleasePlan -BundleDirectory $Fixture.Root -Owner 'idiehl' -Repository 'hypershell-releases' -TargetCommit ('b' * 40)
}
function New-Transport($Plan) {
    $state = @{
        Release = $null; Assets = [Collections.Generic.List[object]]::new(); Mutations = 0; Reads = 0
        Uploads = 0; BadDigest = $false; ExistingTagCommit = $null
    }
    $request = {
        param($Method, $Url, $Body, $File)
        $state.Reads++
        if ($Method -eq 'GET-OPTIONAL') {
            if ($null -eq $state.ExistingTagCommit) { return $null }
            return [pscustomobject]@{ sha = $state.ExistingTagCommit }
        }
        if ($Url -like "*/commits/$($Plan.Tag)") { return [pscustomobject]@{ sha = $state.ExistingTagCommit } }
        if ($Url -like '*/commits/*') { return [pscustomobject]@{ sha = $Plan.TargetCommit } }
        if ($Method -eq 'GET' -and $Url -like '*/releases?per_page*') {
            if ($null -ne $state.Release) { return $state.Release }
            return
        }
        if ($Method -eq 'GET' -and $Url -like '*/assets?per_page*') { return $state.Assets.ToArray() }
        if ($Method -eq 'GET' -and $Url -like '*/releases/42') { return $state.Release }
        if ($Method -eq 'POST' -and $Url -like 'https://api.github.com/*/releases') {
            $state.Mutations++
            $state.Release = [pscustomobject]@{
                id = 42; tag_name = $Body.tag_name; name = $Body.name; body = $Body.body
                target_commitish = $Body.target_commitish; draft = $Body.draft; prerelease = $Body.prerelease
            }
            return $state.Release
        }
        if ($Method -eq 'POST' -and $Url -like 'https://uploads.github.com/*') {
            $state.Mutations++; $state.Uploads++
            $digest = if ($state.BadDigest) { 'sha256:' + ('0' * 64) } else { 'sha256:' + $File.Sha256 }
            $asset = [pscustomobject]@{ name = $File.Name; state = 'uploaded'; size = $File.Size; digest = $digest }
            $state.Assets.Add($asset)
            return $asset
        }
        throw "Unexpected test request: $Method $Url"
    }.GetNewClosure()
    [pscustomobject]@{ State = $state; Request = $request }
}
try {
    $fixture = New-Fixture
    $plan = Get-Plan $fixture
    Require ($plan.Files.Count -eq 5 -and -not $plan.SignatureVerified) 'The coordinated offline plan must contain exactly five unverified files.'
    Require ($plan.SourceCommit -ne $plan.TargetCommit) 'Public distribution tags must not require private source commits in GitHub.'
    $transport = New-Transport $plan
    Reject { Invoke-HypershellGitHubDraft -Plan $plan -Request $transport.Request } 'Signature and Authenticode'
    Require ($transport.State.Reads -eq 0) 'Unverified plans must not contact GitHub.'
    # Only this mocked transport gets a trusted fixture. Production has no skip-verification parameter.
    $plan.SignatureVerified = $true
    $result = Invoke-HypershellGitHubDraft -Plan $plan -Request $transport.Request
    Require ($result.Draft -and $result.VerifiedSha256 -and $result.ArtifactCount -eq 5) 'Draft upload must independently verify all five remote assets.'
    Require ($transport.State.Uploads -eq 5 -and $transport.State.Mutations -eq 6) 'New draft should upload each asset exactly once.'
    $mutations = $transport.State.Mutations
    $null = Invoke-HypershellGitHubDraft -Plan $plan -Request $transport.Request
    Require ($transport.State.Mutations -eq $mutations) 'An identical retry must not modify existing assets.'
    $transport.State.Assets.RemoveAt(4)
    $null = Invoke-HypershellGitHubDraft -Plan $plan -Request $transport.Request
    Require ($transport.State.Mutations -eq ($mutations + 1)) 'An interrupted draft must resume only its missing asset.'
    $transport.State.Release.draft = $false
    Reject { Invoke-HypershellGitHubDraft -Plan $plan -Request $transport.Request } 'already public'
    $transport.State.Release.draft = $true
    $transport.State.Assets[0].digest = 'sha256:' + ('f' * 64)
    Reject { Invoke-HypershellGitHubDraft -Plan $plan -Request $transport.Request } 'does not match'
    Require ($transport.State.Mutations -eq ($mutations + 1)) 'Conflicting assets must never be overwritten.'

    $transport = New-Transport $plan
    $transport.State.ExistingTagCommit = 'c' * 40
    Reject { Invoke-HypershellGitHubDraft -Plan $plan -Request $transport.Request } 'different commit'
    Require ($transport.State.Mutations -eq 0) 'A conflicting tag must not create a draft.'
    $transport = New-Transport $plan
    $transport.State.BadDigest = $true
    Reject { Invoke-HypershellGitHubDraft -Plan $plan -Request $transport.Request } 'does not match'
    Require ($transport.State.Uploads -eq 1) 'Failed remote hash confirmation must stop further uploads.'

    $fixture = New-Fixture
    [IO.File]::AppendAllText((Join-Path $fixture.Root $fixture.Core.artifacts[0].fileName), 'tampered')
    Reject { Get-Plan $fixture } 'invalid type or size'
    $fixture = New-Fixture
    $fixture.Complete.gitCommit = 'd' * 40
    Write-Manifest (Join-Path $fixture.Root 'complete-manifest.json') $fixture.Complete
    Reject { Get-Plan $fixture } 'disagree on gitCommit'
    $fixture = New-Fixture
    Write-Manifest (Join-Path $fixture.Root 'complete-manifest.json') $fixture.Complete ([byte[]](5, 6, 7))
    Reject { Get-Plan $fixture } 'same release signer'
    $fixture = New-Fixture
    $fixture.Complete.artifacts[0].fileName = '../secret.exe'
    Write-Manifest (Join-Path $fixture.Root 'complete-manifest.json') $fixture.Complete
    Reject { Get-Plan $fixture } 'unsafe or duplicate'
    $fixture = New-Fixture
    [IO.File]::WriteAllText((Join-Path $fixture.Root 'unlisted-secret.txt'), 'do not publish')
    Reject { Get-Plan $fixture } 'unlisted entry'
    $fixture = New-Fixture
    $fixture.Core.channel = 'diagnostic'
    Write-Manifest (Join-Path $fixture.Root 'release-manifest.json') $fixture.Core
    Reject { Get-Plan $fixture } 'not a coordinated stable'
    $fixture = New-Fixture
    $plan = Get-Plan $fixture
    $offline = & (Join-Path $PSScriptRoot 'Publish-HypershellGitHubRelease.ps1') -BundleDirectory $fixture.Root -Owner idiehl -Repository hypershell-releases -TargetCommit ('b' * 40) -TrustedSignerSha256 $plan.SignerSha256 -WhatIf
    Require ($offline.Mode -eq 'OfflinePlan' -and -not $offline.SignatureVerified -and $offline.Files.Count -eq 5) 'WhatIf must produce a clearly unverified offline plan without credentials, dotnet, or network access.'
    Write-Output "HYPERSHELL_GITHUB_RELEASE_TESTS_OK|checks:$checks|offline|draft-only|no-clobber|remote-digests|resume"
}
finally {
    $resolvedRoot = [IO.Path]::GetFullPath($testRoot)
    $approvedPrefix = $testParent.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedRoot.StartsWith($approvedPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'The test cleanup escaped its isolated directory.' }
    if (Test-Path -LiteralPath $resolvedRoot) { Remove-Item -LiteralPath $resolvedRoot -Recurse -Force }
}
