#Requires -Version 7.0
[CmdletBinding()]
param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if (-not $OutputPath) { $OutputPath = Join-Path $repoRoot 'output/mcp-secure-tunnel-20261023/task-launcher' }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$profileRoot = Join-Path $env:LOCALAPPDATA 'AtlasMeridian/McpCompanion'
$backendShell = [IO.Path]::GetFullPath((Join-Path $profileRoot 'Stable/bin/PowerShell7/pwsh.exe'))
$secureShell = [IO.Path]::GetFullPath((Join-Path $profileRoot 'SecureTunnel/bin/PowerShell7/pwsh.exe'))
$hash = $null
foreach ($shell in @($backendShell,$secureShell)) {
    if (-not (Test-Path -LiteralPath $shell -PathType Leaf)) { throw 'A fixed private PowerShell installation is missing.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $shell
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw 'The fixed PowerShell binary must retain its valid Microsoft signature.' }
    $currentHash = (Get-FileHash -LiteralPath $shell -Algorithm SHA256).Hash
    if ($hash -and $hash -ne $currentHash) { throw 'The two fixed PowerShell installations do not match.' }
    $hash = $currentHash
}
$project = Join-Path $PSScriptRoot '../launcher/Meridian.TaskLauncher.csproj'
& dotnet publish $project -c Release -o $OutputPath "-p:BackendPowerShell=$backendShell" "-p:SecurePowerShell=$secureShell" "-p:PowerShellSha256=$hash" --nologo
if ($LASTEXITCODE -ne 0) { throw 'The isolated no-console launcher build failed.' }
$binary = Join-Path $OutputPath 'Meridian.TaskLauncher.exe'
$launcherHash=(Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash
$receipt = @{ schema='atlas-meridian-task-launcher-v1'; sourceRoot=$repoRoot; backendPowerShell=$backendShell; securePowerShell=$secureShell; powerShellSha256=$hash; launcherSha256=$launcherHash; singleFile=$true; bundledComponents=@('Meridian.TaskLauncher.dll','Meridian.TaskLauncher.runtimeconfig.json','Meridian.TaskLauncher.deps.json'); files=@{'Meridian.TaskLauncher.exe'=$launcherHash}; modes=@('backend','secure-tunnel','setup'); runtime='Microsoft.WindowsDesktop.App 8.0'; builtAt=[DateTime]::UtcNow.ToString('o') }
[IO.File]::WriteAllText((Join-Path $OutputPath 'build-manifest.json'), ($receipt | ConvertTo-Json -Depth 5))
[pscustomobject]@{ output=$OutputPath; launcherSha256=$receipt.launcherSha256; deployed=$false; tasksChanged=$false } | ConvertTo-Json
