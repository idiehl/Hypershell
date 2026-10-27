[CmdletBinding()]
param([Parameter(Mandatory=$true)][int]$ExpectedProcessId,[switch]$RebuildCandidate)
. (Join-Path $PSScriptRoot 'common.ps1')
$installedBefore = Application-Inventory $installed
$hostBefore = Host-Identity $ExpectedProcessId
$nativeBefore = Native-Identity; Assert-ExpectedNative $nativeBefore
$manifestHash = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash
$sourceBefore = Source-Inventory
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
[ordered]@{ passed=$false; pending=$true; reason='Candidate preparation invalidates any earlier review.'; originalSavedDataFilesAccessed=$false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'package-review.json') -Encoding utf8NoBOM
if (Test-Path -LiteralPath $stage) {
    if (!$RebuildCandidate) { throw 'Candidate already exists; preserve it or explicitly request -RebuildCandidate.' }
    $archive = Join-Path $packageRoot ('app-before-rebuild-' + (Get-Date -Format 'yyyyMMdd-HHmmssffff'))
    $null = Resolve-ApplicationFile $packageRoot 'app'
    $null = Resolve-ApplicationFile $packageRoot ([IO.Path]::GetFileName($archive))
    $null = Application-Inventory $stage
    Move-Item -LiteralPath $stage -Destination $archive
}
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$isolatedProjectRoot = Join-Path $packageRoot 'host-project'
New-Item -ItemType Directory -Path $isolatedProjectRoot -Force | Out-Null
$isolatedProject = Join-Path $isolatedProjectRoot 'Meridian.Host.csproj'
$projectXml = @"
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><AssemblyName>Meridian.Host</AssemblyName><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ApplicationIcon>$workspace/assets/brand/meridian.ico</ApplicationIcon></PropertyGroup>
  <ItemGroup><Compile Include="$workspace/src/host/*.cs" /><Content Include="$workspace/dist/**/*" Link="wwwroot/%(RecursiveDir)%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" /><Content Include="$workspace/assets/brand/meridian.png;$workspace/assets/brand/meridian.ico" Link="assets/brand/%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" /></ItemGroup>
</Project>
"@
Set-Content -LiteralPath $isolatedProject -Value $projectXml -Encoding utf8NoBOM
Push-Location -LiteralPath $workspace
try {
    # Root has completed the final browser build before this step. Publish is
    # isolated from the installed app and does not generate a launcher manifest.
    & dotnet publish $isolatedProject -c Release -r win-x64 --self-contained true -o $stage --nologo
    if ($LASTEXITCODE -ne 0) { throw 'The isolated full host publish failed.' }
} finally { Pop-Location }
if (!(Test-Path -LiteralPath (Join-Path $stage 'Meridian.Host.exe'))) { throw 'The candidate executable is missing.' }
$candidate = Application-Inventory $stage
$candidatePaths = @{}; foreach ($file in $candidate) { $candidatePaths[$file.relativePath]=$true }
$preservedCompatibilityFiles = @()
foreach ($file in $installedBefore | Where-Object { !$candidatePaths.ContainsKey($_.relativePath) }) {
    # The current SDK may omit the version-named diagnostic DAC alias supplied
    # by an earlier publish. Preserve its exact application bytes; do not delete
    # any installed file or silently retain unrelated obsolete host content.
    if ($file.relativePath -notmatch '^mscordaccore_amd64_amd64_[0-9.]+\.dll$') { throw "Fresh publish omitted an unreviewed application file: $($file.relativePath)" }
    $original = Resolve-ApplicationFile $installed $file.relativePath
    if ((Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash -cne $file.sha256) { throw 'Compatibility application file changed during preparation.' }
    Copy-Item -LiteralPath $original -Destination (Resolve-ApplicationFile $stage $file.relativePath)
    $preservedCompatibilityFiles += $file
}
$candidate = Application-Inventory $stage
Assert-ApplicationFileSet $installedBefore $candidate
Assert-FinalDist; Assert-Inventory $installed $installedBefore
if (!(Equal-Json $hostBefore (Host-Identity $ExpectedProcessId)) -or !(Equal-Json $nativeBefore (Native-Identity)) -or $manifestHash -cne (Get-FileHash -LiteralPath $manifest).Hash -or !(Equal-Json $sourceBefore (Source-Inventory))) { throw 'Protected identity or production source changed during preparation.' }
[ordered]@{ preparedAt=[DateTimeOffset]::Now.ToString('o'); mode='full-host-fixture-only'; packageRoot=$stage; packageHashes=$candidate; installedHashes=$installedBefore; host=$hostBefore; native=$nativeBefore; manifestHash=$manifestHash; sourceHashes=$sourceBefore; preservedCompatibilityFiles=$preservedCompatibilityFiles; savedDataFilesAccessed=$false; installedFilesChanged=$false; backendRestarted=$false; nativeRestarted=$false } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'preparation.json') -Encoding utf8NoBOM
Write-Output "Prepared $($candidate.Count) isolated application files including the updated host schema. Live backend and native windows remain untouched."

