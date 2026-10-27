[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$HypershellAppDirectory,
    [string]$ApplicationDataDirectory = (Join-Path $env:LOCALAPPDATA 'Hypershell'),
    [string]$ManifestPath,
    [switch]$Apply,
    [string]$ExpectedRegistrySha256
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $ManifestPath) { $ManifestPath = Join-Path $projectRoot 'output\meridian.app.json' }
$liveAppDirectory = (Resolve-Path -LiteralPath $HypershellAppDirectory).Path
$harnessOutput = Join-Path $projectRoot '.cache\register-mynet\app'
$harnessProject = Join-Path $PSScriptRoot 'register-mynet\RegisterMyNet.csproj'
& dotnet build $harnessProject --output $harnessOutput --nologo "-p:HypershellAppDirectory=$liveAppDirectory"
if ($LASTEXITCODE -ne 0) { throw 'The MyNet importer harness could not be built.' }
$harnessArguments = @($liveAppDirectory, [IO.Path]::GetFullPath($ApplicationDataDirectory), [IO.Path]::GetFullPath($ManifestPath))
if ($Apply) { $harnessArguments += @('--apply', '--expected-registry-sha256', $ExpectedRegistrySha256) }
& dotnet (Join-Path $harnessOutput 'RegisterMyNet.dll') @harnessArguments
if ($LASTEXITCODE -ne 0) { throw 'MyNet registration did not complete. Review the preflight error above.' }
