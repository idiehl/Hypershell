[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$parent = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../output/suite-payload-tests'))
$fixture = Join-Path $parent ([Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$validator = Join-Path $PSScriptRoot 'Test-HypershellSuitePayload.ps1'
$required = @('Authoring/studio/Hypershell.exe', 'Authoring/studio/Hypershell.dll', 'Authoring/Hyperlib/hyperlib.app.json',
    'Authoring/Hyperlib/app/HyperLib.Host.exe', 'Authoring/Hyperlib/src/HyperLib.Core/HyperLib.Core.csproj',
    'Authoring/Hyperlib/src/HyperLib.Runtime/HyperLib.Runtime.csproj',
    'Authoring/Hyperlib/Starters/standard/src/Host/Host.csproj',
    'Authoring/Hyperlib/Starters/standard/src/Host/Program.cs',
    'Authoring/Hyperlib/Starters/standard/src/Pages/Pages.csproj', 'Meridian/Meridian.Host.exe', 'Meridian/wwwroot/index.html')
$checks = 0
function Reject([scriptblock] $Action, [string] $Expected) {
    try { & $Action | Out-Null }
    catch {
        if ($_.Exception.Message -notlike "*$Expected*") { throw }
        $script:checks++
        return
    }
    throw "Expected rejection: $Expected"
}
function WriteReceipt {
    $script:receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $fixture 'suite-payload.json') -Encoding utf8NoBOM
}
try {
    $entries = @($required | ForEach-Object {
        $path = Join-Path $fixture $_
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
        [IO.File]::WriteAllText($path, 'inert test fixture')
        @{ path = $_; bytes = (Get-Item -LiteralPath $path).Length; sha256 = (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() }
    })
    $receipt = @{ schemaVersion = 1; sources = @{ sourceKind = 'clean-git-archive'; version = '1.2.3'; architecture = 'win-x64'; studioCommit = ('a' * 40); meridianCommit = ('b' * 40) }; files = $entries }
    WriteReceipt
    & $validator -PayloadDirectory $fixture -ExpectedVersion '1.2.3'
    $checks++
    Reject { & $validator -PayloadDirectory $fixture -ExpectedVersion '1.2.4' } 'version differs'
    $missingStarter = 'Authoring/Hyperlib/Starters/standard/src/Host/Host.csproj'
    $receipt.files = @($entries | Where-Object { $_.path -ne $missingStarter })
    Remove-Item -LiteralPath (Join-Path $fixture $missingStarter)
    WriteReceipt
    Reject { & $validator -PayloadDirectory $fixture } 'Complete payload is missing'
    [IO.File]::WriteAllText((Join-Path $fixture $missingStarter), 'inert test fixture')
    $receipt.files = $entries
    WriteReceipt
    $tampered = Join-Path $fixture $required[0]
    [IO.File]::AppendAllText($tampered, 'tampered')
    Reject { & $validator -PayloadDirectory $fixture } 'hash or length mismatch'
    [IO.File]::WriteAllText($tampered, 'inert test fixture')
    $extra = Join-Path $fixture 'Authoring/account-secret.json'
    [IO.File]::WriteAllText($extra, 'fixture')
    Reject { & $validator -PayloadDirectory $fixture } 'Unlisted payload'
    Remove-Item -LiteralPath $extra
    $receipt.files += $entries[0]
    WriteReceipt
    Reject { & $validator -PayloadDirectory $fixture } 'duplicate payload'
    $receipt.files = $entries
    $original = $entries[0].path
    $entries[0].path = 'Authoring/../../escape.exe'
    WriteReceipt
    Reject { & $validator -PayloadDirectory $fixture } 'Unsafe'
    $entries[0].path = $original
    $receipt.sources.sourceKind = 'working-directory'
    WriteReceipt
    Reject { & $validator -PayloadDirectory $fixture } 'provenance'
    # Git archives can flatten symbolic links into ordinary files on Windows.
    # Reject their committed mode before extraction or any build takes place.
    $linkedSource = Join-Path $fixture 'linked-source'
    [IO.Directory]::CreateDirectory($linkedSource) | Out-Null
    [IO.File]::WriteAllText((Join-Path $linkedSource 'target.txt'), 'fixture')
    $sourceLink = Join-Path $linkedSource 'link.txt'
    [IO.File]::CreateSymbolicLink($sourceLink, 'target.txt') | Out-Null
    try {
        & git -C $linkedSource init -q
        if ($LASTEXITCODE -ne 0) { throw 'Cannot create the source-link fixture repository.' }
        & git -C $linkedSource -c core.symlinks=true add target.txt link.txt
        if ($LASTEXITCODE -ne 0) { throw 'Cannot stage the source-link fixture.' }
        & git -C $linkedSource -c user.name='Hypershell Test' -c user.email='hypershell-test@example.invalid' commit -q -m 'Inert source-link fixture'
        if ($LASTEXITCODE -ne 0) { throw 'Cannot commit the source-link fixture.' }
        $builder = Join-Path $PSScriptRoot 'New-HypershellSuitePayload.ps1'
        Reject { & $builder -Version '1.2.3' -StudioSourceRoot $linkedSource -MeridianSourceRoot $linkedSource -OutputDirectory (Join-Path $fixture 'rejected-output') } 'Committed symbolic links'
        if (Test-Path -LiteralPath (Join-Path $fixture 'rejected-output')) { throw 'Source-link rejection must precede artifact creation.' }
    }
    finally { [IO.File]::Delete($sourceLink) }
    Write-Output "HYPERSHELL_SUITE_PAYLOAD_TESTS_OK|checks:$checks"
}
finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (-not $resolved.StartsWith($parent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture cleanup escaped test root.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
