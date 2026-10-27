$ErrorActionPreference = 'Stop'

$nativeExecutable = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Hypershell\bin\Hypershell.exe'
$previewProfile = 'I:\dev\.codex-build\hyperlib-authoring\native-profile'

if (-not (Test-Path -LiteralPath $nativeExecutable -PathType Leaf)) {
    throw "The Hypershell launcher was not found at '$nativeExecutable'. Install it with tools/Install-HypershellLauncher.ps1 from the native source checkout. You can still open the local web application with Start-HyperLib.ps1 -OpenBrowser."
}

$libraryRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
[System.IO.Directory]::CreateDirectory($previewProfile) | Out-Null
$launch = [System.Diagnostics.ProcessStartInfo]::new()
$launch.FileName = $nativeExecutable
$launch.WorkingDirectory = [System.IO.Path]::GetDirectoryName($nativeExecutable)
$launch.UseShellExecute = $false
$launch.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Normal
$launch.Environment['HYPERLIB_ROOT'] = $libraryRoot
$launch.Environment['HYPERSHELL_APP_DATA'] = $previewProfile
$process = [System.Diagnostics.Process]::Start($launch)
if ($null -eq $process) {
    throw 'Windows could not start the HyperLib companion build.'
}

Write-Host 'Opened the HyperLib companion build with a separate Hypershell profile.'
Write-Host 'Choose Continue without signing in, then MyNet > Apps > HyperLib.'
Write-Host 'Configure AI Access and your AI Context in this profile when you want to use the assistant.'
