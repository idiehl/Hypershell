param([string]$OutputDirectory, [string]$ArtifactsPath)
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'output/spiral-shell-20260928/icons'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$project = Join-Path $PSScriptRoot 'Hypershell.BrandRenderer/Hypershell.BrandRenderer.csproj'
if ([string]::IsNullOrWhiteSpace($ArtifactsPath)) {
    $ArtifactsPath = Join-Path $repositoryRoot 'output/.brand-renderer-artifacts'
}
dotnet run --project $project -c Release --artifacts-path $ArtifactsPath -- $repositoryRoot $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Direct vector rendering or native/SVG parity verification failed.' }
