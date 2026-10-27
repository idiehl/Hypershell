[CmdletBinding()]
param([Parameter(Mandatory)][string] $PayloadDirectory, [string] $ExpectedVersion)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath($PayloadDirectory)
$receiptPath = Join-Path $root 'suite-payload.json'
if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) { throw 'A source-provenance and payload-hash receipt is required.' }
$receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
if ($receipt.schemaVersion -ne 1 -or $receipt.sources.sourceKind -ne 'clean-git-archive' -or
    $receipt.sources.architecture -ne 'win-x64' -or $receipt.sources.version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$' -or $receipt.sources.studioCommit -notmatch '^[a-f0-9]{40}$' -or
    $receipt.sources.meridianCommit -notmatch '^[a-f0-9]{40}$') { throw 'The payload source provenance is invalid.' }
if ($ExpectedVersion -and $receipt.sources.version -ne $ExpectedVersion) { throw 'The staged payload version differs from the release version.' }
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($file in $receipt.files) {
    if ($file.path -notmatch '^(Authoring|Meridian)/' -or $file.path -match '(^|/)\.\.(/|$)|[:\\]' -or -not $seen.Add($file.path)) { throw 'Unsafe or duplicate payload path.' }
    $path = Join-Path $root $file.path
    for ($current = $path; $null -ne $current; $current = [IO.Path]::GetDirectoryName($current)) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Payload links are not permitted.' }
    }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne $file.bytes -or
        $file.sha256 -notmatch '^[a-f0-9]{64}$' -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $file.sha256) { throw "Payload hash or length mismatch: $($file.path)" }
}
foreach ($file in Get-ChildItem -LiteralPath $root -Force -Recurse -File) {
    $relative = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
    if ($relative -ne 'suite-payload.json' -and -not $seen.Contains($relative)) { throw "Unlisted payload file: $relative" }
}
foreach ($required in @('Authoring/studio/Hypershell.exe', 'Authoring/studio/Hypershell.dll', 'Authoring/Hyperlib/hyperlib.app.json',
    'Authoring/Hyperlib/app/HyperLib.Host.exe', 'Authoring/Hyperlib/src/HyperLib.Core/HyperLib.Core.csproj',
    'Authoring/Hyperlib/src/HyperLib.Runtime/HyperLib.Runtime.csproj',
    'Authoring/Hyperlib/Starters/standard/src/Host/Host.csproj',
    'Authoring/Hyperlib/Starters/standard/src/Host/Program.cs',
    'Authoring/Hyperlib/Starters/standard/src/Pages/Pages.csproj', 'Meridian/Meridian.Host.exe', 'Meridian/wwwroot/index.html')) {
    if (-not $seen.Contains($required)) { throw "Complete payload is missing $required" }
}
Write-Output "HYPERSHELL_SUITE_PAYLOAD_VALID|files:$($seen.Count)"
