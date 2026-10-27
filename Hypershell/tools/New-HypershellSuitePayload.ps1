[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')][string] $Version,
    [Parameter(Mandatory)][string] $StudioSourceRoot,
    [Parameter(Mandatory)][string] $MeridianSourceRoot,
    [Parameter(Mandatory)][string] $OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$destination = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $destination) { throw 'Choose a new payload directory; existing output is never replaced.' }

function Invoke-Checked([string] $Command, [string[]] $Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE." }
}
function Get-CleanCommit([string] $Source) {
    $changes = @(& git -C $Source status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0 -or $changes.Count -ne 0) { throw "Commit the intended source before preparing a release: $Source" }
    $root = (& git -C $Source rev-parse --show-toplevel).Trim()
    if ($LASTEXITCODE -ne 0 -or [IO.Path]::GetFullPath($root) -ine [IO.Path]::GetFullPath($Source)) { throw 'Source paths must identify repository roots.' }
    $commit = (& git -C $Source rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[a-f0-9]{40}$') { throw 'A committed source identity is required.' }
    $entries = @(& git -C $Source ls-tree -r --full-tree $commit)
    if ($LASTEXITCODE -ne 0) { throw 'The committed source tree could not be inspected.' }
    if (@($entries | Where-Object { $_ -match '^120000\s' }).Count -gt 0) {
        throw 'Committed symbolic links are not supported in release source archives.'
    }
    return $commit
}
function Copy-CuratedSource([string] $Source, [string] $Target) {
    if (-not [IO.Directory]::Exists($Source)) { throw "A required stock source directory is missing: $Source" }
    for ($ancestor = [IO.Path]::GetFullPath($Source); $ancestor; $ancestor = [IO.Path]::GetDirectoryName($ancestor)) {
        if (((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Stock source paths cannot contain symbolic links or junctions.'
        }
    }
    [IO.Directory]::CreateDirectory($Target) | Out-Null
    foreach ($entry in Get-ChildItem -LiteralPath $Source -Force) {
        if ($entry.PSIsContainer -and $entry.Name -in @('bin', 'obj')) { continue }
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Stock source contains a symbolic link or junction.' }
        if (($entry.Attributes -band [IO.FileAttributes]::Hidden) -ne 0 -or
            $entry.Name -in @('.git', '.hyperlib', '.visual-page', '.hypershell', '.cache', 'node_modules', '.env', 'secrets.json', 'appsettings.Local.json') -or
            $entry.Name.StartsWith('.env.') -or $entry.Extension -in @('.pfx', '.p12', '.pem', '.key', '.log', '.bak', '.tmp')) {
            throw "Private or generated state is not stock source: $($entry.FullName)"
        }
        $targetPath = Join-Path $Target $entry.Name
        if ($entry.PSIsContainer) { Copy-CuratedSource $entry.FullName $targetPath }
        else { Copy-Item -LiteralPath $entry.FullName -Destination $targetPath }
    }
}
$studioCommit = Get-CleanCommit $StudioSourceRoot
$meridianCommit = Get-CleanCommit $MeridianSourceRoot
[IO.Directory]::CreateDirectory($destination) | Out-Null
$work = Join-Path $destination 'build'
$payload = Join-Path $destination 'payload'
[IO.Directory]::CreateDirectory($work) | Out-Null

foreach ($source in @(
    @{ Name = 'studio'; Root = $StudioSourceRoot; Commit = $studioCommit },
    @{ Name = 'meridian'; Root = $MeridianSourceRoot; Commit = $meridianCommit }
)) {
    $archive = Join-Path $work ($source.Name + '.zip')
    Invoke-Checked 'git' @('-C', $source.Root, 'archive', '--format=zip', "--output=$archive", $source.Commit)
    Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $work $source.Name)
}
$studioSnapshot = Join-Path $work 'studio'
$meridianSnapshot = Join-Path $work 'meridian'
foreach ($required in @('src/Hypershell/Services/StandaloneStudioHost.cs', 'Hyperlib/src/HyperLib.Host/HyperLib.Host.csproj')) {
    if (-not (Test-Path -LiteralPath (Join-Path $studioSnapshot $required))) { throw "The committed Studio source is incomplete: $required" }
}
$studioUpdater = Get-Content -LiteralPath (Join-Path $studioSnapshot 'src/Hypershell/Services/ApplicationUpdateService.cs') -Raw
if (-not $studioUpdater.Contains('DetectSuiteComponents') -or -not $studioUpdater.Contains('ManualSuiteUpdate')) {
    throw 'Integrate the Complete update guard into the committed Studio source before releasing its standalone binary.'
}
$studioProjects = Get-Content -LiteralPath (Join-Path $studioSnapshot 'src/Hypershell/Services/VisualPageProjectService.cs') -Raw
if (-not $studioProjects.Contains('DetectSuiteComponents')) {
    throw 'Integrate the per-user Complete projects directory into the committed Studio source before releasing.'
}
$authoring = Join-Path $payload 'Authoring'
$meridian = Join-Path $payload 'Meridian'
$library = Join-Path $authoring 'Hyperlib'
Invoke-Checked 'dotnet' @('publish', (Join-Path $studioSnapshot 'src/Hypershell/Hypershell.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:DebugType=None', '-p:DebugSymbols=false', "-p:Version=$Version", '-o', (Join-Path $authoring 'studio'))
Invoke-Checked 'dotnet' @('publish', (Join-Path $studioSnapshot 'Hyperlib/src/HyperLib.Host/HyperLib.Host.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:DebugType=None', '-p:DebugSymbols=false', "-p:Version=$Version", '-o', (Join-Path $library 'app'))

# Seed the runtime source SDK, standard source starter, and app descriptor.
# The standard starter is required to generate the stock catalog and library
# on first launch. Authored Works/Pages/media,
# journals, local accounts, caches and dev registrations never enter the package.
Copy-Item -LiteralPath (Join-Path $studioSnapshot 'Hyperlib/hyperlib.app.json') -Destination $library
foreach ($project in @('src/HyperLib.Core', 'src/HyperLib.Runtime', 'Starters/standard')) {
    Copy-CuratedSource (Join-Path $studioSnapshot "Hyperlib/$project") (Join-Path $library $project)
}
Push-Location $meridianSnapshot
try {
    Invoke-Checked 'npm.cmd' @('ci')
    Invoke-Checked 'npm.cmd' @('run', 'build')
    Invoke-Checked 'dotnet' @('publish', './src/host/Meridian.Host.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:DebugType=None', '-p:DebugSymbols=false', "-p:Version=$Version", '-o', $meridian)
}
finally { Pop-Location }
$provenance = [ordered]@{ schemaVersion = 1; version = $Version; studioCommit = $studioCommit; meridianCommit = $meridianCommit; architecture = 'win-x64'; sourceKind = 'clean-git-archive' }
foreach ($directory in @($authoring, $meridian)) {
    $provenance | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'source-provenance.json') -Encoding utf8NoBOM
}
$files = @(Get-ChildItem -LiteralPath $payload -File -Recurse | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path = [IO.Path]::GetRelativePath($payload, $_.FullName).Replace('\', '/'); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
[ordered]@{ schemaVersion = 1; sources = $provenance; files = $files } | ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath (Join-Path $payload 'suite-payload.json') -Encoding utf8NoBOM
& (Join-Path $PSScriptRoot 'Test-HypershellSuitePayload.ps1') -PayloadDirectory $payload
Write-Output "HYPERSHELL_SUITE_PAYLOAD_READY|$payload"
