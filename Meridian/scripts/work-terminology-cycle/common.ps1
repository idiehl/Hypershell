$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ($workspace -ne 'I:\dev\Meridian') { throw 'Unexpected Meridian workspace.' }
$installed = Join-Path $workspace 'output/app'
$packageRoot = Join-Path $workspace 'output/work-terminology-20261019/package'
$stage = Join-Path $packageRoot 'app'
$evidence = Join-Path $packageRoot 'qa'
$manifest = Join-Path $workspace 'output/meridian.app.json'
$staticNames = @('index.html','editor.js','editor.js.map','viewer.js','app.css','scene.css')
function Resolve-ApplicationFile([string]$Root,[string]$Relative) {
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    if (!$rootPath.StartsWith($workspace + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Application root escaped the workspace.' }
    $resolved = [IO.Path]::GetFullPath((Join-Path $rootPath $Relative))
    if (!$resolved.StartsWith($rootPath + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Application path escaped its root.' }
    $probe = $resolved
    while ($probe.Length -ge $workspace.Length) {
        if ((Test-Path -LiteralPath $probe) -and ((Get-Item -LiteralPath $probe -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Application links are not eligible: $probe" }
        if ($probe -eq $workspace) { break }; $probe = [IO.Path]::GetDirectoryName($probe)
    }
    return $resolved
}
function Application-Inventory([string]$Root) {
    $resolved = [IO.Path]::GetFullPath($Root)
    if (!$resolved.StartsWith($workspace + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Inventory escaped the workspace.' }
    $items = @(Get-ChildItem -LiteralPath $Root -Recurse -Force)
    if ((Get-Item -LiteralPath $Root).Attributes -band [IO.FileAttributes]::ReparsePoint -or @($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Inventory cannot follow filesystem links.' }
    @($items | Where-Object { !$_.PSIsContainer } | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($resolved,$_.FullName)
        [pscustomobject]@{ relativePath = $relative; sha256 = (Get-FileHash -LiteralPath (Resolve-ApplicationFile $Root $relative) -Algorithm SHA256).Hash }
    } | Sort-Object relativePath)
}
function Source-Inventory {
    $files = @(Get-ChildItem -LiteralPath (Join-Path $workspace 'src') -Recurse -File | Where-Object {
        [IO.Path]::GetRelativePath($workspace,$_.FullName) -notmatch '(?:^|\\)(?:bin|obj)(?:\\|$)' -and $_.Extension -in @('.ts','.css','.cs','.csproj','.json')
    } | Select-Object -ExpandProperty FullName)
    $files += @('package.json','package-lock.json','scripts/build.mjs') | ForEach-Object { Join-Path $workspace $_ }
    @($files | ForEach-Object { [pscustomobject]@{ relativePath=[IO.Path]::GetRelativePath($workspace,$_); sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } } | Sort-Object relativePath)
}
function Equal-Json($A,$B) { ($A | ConvertTo-Json -Depth 8 -Compress) -ceq ($B | ConvertTo-Json -Depth 8 -Compress) }
function Native-Identity { @(Get-CimInstance Win32_Process -Filter "Name='Hypershell.exe'" | Where-Object CommandLine -Match '(?:^|\s)--mynet-desktop(?:\s|$)' | Select-Object ProcessId,ExecutablePath,CommandLine,CreationDate | Sort-Object ProcessId) }
function Assert-ExpectedNative($Native) { if (!(Equal-Json @($Native.ProcessId | Sort-Object) @(21144,176948))) { throw 'The expected MyNet windows changed; leave them untouched.' } }
function Host-Identity([int]$ExpectedId) {
    $found = @(Get-CimInstance Win32_Process -Filter "Name='Meridian.Host.exe'" | Where-Object ExecutablePath -EQ (Join-Path $installed 'Meridian.Host.exe'))
    $owners = @(Get-NetTCPConnection -LocalPort 5240 -State Listen | Select-Object -ExpandProperty OwningProcess -Unique)
    if ($found.Count -ne 1 -or $found[0].ProcessId -ne $ExpectedId -or $owners.Count -ne 1 -or $owners[0] -ne $ExpectedId -or
        $found[0].CommandLine -notmatch '^"?[^\r\n]+Meridian\.Host\.exe"?\s+--urls\s+http://127\.0\.0\.1:5240\s*$') { throw 'The installed backend identity changed; leave it running.' }
    $found[0] | Select-Object ProcessId,ExecutablePath,CommandLine,CreationDate
}
function Assert-Inventory([string]$Root,$Expected) { if (!(Equal-Json (Application-Inventory $Root) $Expected)) { throw "Application bytes changed: $Root" } }
function Assert-FinalDist {
    foreach ($name in $staticNames) {
        if ((Get-FileHash -LiteralPath (Resolve-ApplicationFile $stage "wwwroot/$name")).Hash -cne (Get-FileHash -LiteralPath (Resolve-ApplicationFile (Join-Path $workspace 'dist') $name)).Hash) { throw "Candidate differs from final dist: $name" }
    }
}
function Assert-ApplicationFileSet($Original,$Candidate) {
    if (!(Equal-Json @($Original.relativePath) @($Candidate.relativePath))) { throw 'Application file sets differ; review an explicit file-set migration before installing.' }
}






