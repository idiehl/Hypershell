[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'output' }
$packageRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$appDirectory = Join-Path $packageRoot 'app'
New-Item -ItemType Directory -Path $appDirectory -Force | Out-Null
Push-Location -LiteralPath $projectRoot
try {
    & npm run build
    if ($LASTEXITCODE -ne 0) { throw 'Meridian browser build failed.' }
    & dotnet publish (Join-Path $projectRoot 'src/host/Meridian.Host.csproj') -c $Configuration -r win-x64 --self-contained true -o $appDirectory --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Meridian host publish failed.' }
} finally { Pop-Location }
$executable = Join-Path $appDirectory 'Meridian.Host.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'The published Meridian executable is missing.' }
$iconPath = Join-Path $appDirectory 'assets/brand/meridian.png'
$executableIconPath = Join-Path $appDirectory 'assets/brand/meridian.ico'
foreach ($brandFile in @($iconPath, $executableIconPath)) {
    if (-not (Test-Path -LiteralPath $brandFile -PathType Leaf)) { throw "The packaged Meridian brand asset is missing: $brandFile" }
    $sourceBrandFile = Join-Path $projectRoot ('assets/brand/' + [System.IO.Path]::GetFileName($brandFile))
    if ((Get-FileHash -LiteralPath $brandFile -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $sourceBrandFile -Algorithm SHA256).Hash) {
        throw "The packaged Meridian brand asset differs from its source: $brandFile"
    }
}
$manifest = [ordered]@{
    schemaVersion = 1
    apps = @([ordered]@{
        id = 'atlas.meridian'
        displayName = 'Atlas Meridian'
        iconPath = $iconPath
        urlTemplate = 'http://127.0.0.1:{port}/'
        ports = @(5240..5249)
        launch = [ordered]@{
            executable = $executable
            arguments = @('--urls', 'http://127.0.0.1:{port}')
            workingDirectory = $appDirectory
            environment = @{}
        }
        healthCheck = [ordered]@{
            path = '/__meridian_status'
            expectedStatusCode = 200
            headerName = 'X-Atlas-Meridian'
            headerValue = 'true'
            timeoutSeconds = 15
        }
        stop = @{ mode = 'ManagedProcess' }
        allowExternalResources = $false
        showNavigationToolbar = $false
    })
}
$manifestPath = Join-Path $packageRoot 'meridian.app.json'
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
Write-Output "Packaged Atlas Meridian: $appDirectory"
Write-Output "In MyNet, import $manifestPath, review and trust its launch command, then open Atlas Meridian."
Write-Output 'The manifest contains this package location. Regenerate it after moving the package.'
