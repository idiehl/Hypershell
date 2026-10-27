$ErrorActionPreference = 'Stop'
$libraryRoot = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$executable = [IO.Path]::GetFullPath((Join-Path $libraryRoot 'app/HyperLib.Host.exe'))
$matches = @(Get-CimInstance Win32_Process -Filter "Name='HyperLib.Host.exe'" | Where-Object {
    $_.ExecutablePath -eq $executable -and $_.CommandLine.Contains('--root "' + $libraryRoot + '"')
})
foreach ($instance in $matches) {
    Stop-Process -Id $instance.ProcessId
    Write-Output "Stopped HyperLib host $($instance.ProcessId). Its owned renderers stop with it."
}
if ($matches.Count -eq 0) { Write-Output 'No standalone published HyperLib host is running for this folder.' }
