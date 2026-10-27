param(
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$SourceScript = (Join-Path $PSScriptRoot '../Start-HyperLib.ps1')
)
$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath($SourceScript)
$evidence = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) ('start-launcher-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$checks = [Collections.Generic.List[string]]::new()
$global:HyperlibLauncherTestLaunchCalls = [Collections.Generic.List[object]]::new()
$global:HyperlibLauncherTestBuildCalls = 0
$global:HyperlibLauncherTestHealthCalls = 0
$global:HyperlibLauncherTestStarted = $false
$global:HyperlibLauncherTestExistingHealth = $false
$global:HyperlibLauncherTestHealthRoot = ''
$global:HyperlibLauncherTestListenerExecutable = ''
$global:HyperlibLauncherTestFixture = ''

function Check([bool]$Condition, [string]$Description) {
    if (-not $Condition) { throw "FAILED: $Description" }
    $checks.Add($Description)
}
# All network and process calls are synthetic. No application or listener is started or stopped.
function Invoke-RestMethod {
    [CmdletBinding()]param($Uri, $TimeoutSec)
    $global:HyperlibLauncherTestHealthCalls++
    if (-not $global:HyperlibLauncherTestExistingHealth -and -not $global:HyperlibLauncherTestStarted) { throw 'No synthetic listener yet.' }
    return [pscustomobject]@{ app = 'hyperlib'; root = $global:HyperlibLauncherTestHealthRoot }
}
function Get-NetTCPConnection {
    [CmdletBinding()]param($LocalPort, $State)
    return [pscustomobject]@{ LocalAddress = '127.0.0.1'; OwningProcess = 123456 }
}
function Get-Process {
    [CmdletBinding()]param($Id)
    return [pscustomobject]@{ Id = $Id; Path = $global:HyperlibLauncherTestListenerExecutable }
}
function Start-Process {
    [CmdletBinding()]param($FilePath, $ArgumentList, $WorkingDirectory, $WindowStyle, [switch]$PassThru, $RedirectStandardOutput, $RedirectStandardError)
    $global:HyperlibLauncherTestLaunchCalls.Add([pscustomobject]@{ path = $FilePath; windowStyle = $WindowStyle; arguments = $ArgumentList })
    $global:HyperlibLauncherTestStarted = $true
    $global:HyperlibLauncherTestListenerExecutable = $FilePath
    return [pscustomobject]@{ Id = 123456; HasExited = $false }
}
function Stop-Process { throw 'The launcher must never stop an existing session.' }
function Start-Sleep { throw 'A synthetic ready listener should not require a delay.' }
function dotnet {
    $global:HyperlibLauncherTestBuildCalls++
    [IO.File]::WriteAllText((Join-Path $global:HyperlibLauncherTestFixture 'app/HyperLib.Host.exe'), 'synthetic legacy host')
    $global:LASTEXITCODE = 0
}
function New-Fixture([string]$Name, [bool]$WithPointer = $true) {
    $global:HyperlibLauncherTestFixture = Join-Path $evidence $Name
    New-Item -ItemType Directory -Path (Join-Path $global:HyperlibLauncherTestFixture 'app/releases/review-v2') -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination (Join-Path $global:HyperlibLauncherTestFixture 'Start-HyperLib.ps1')
    [IO.File]::WriteAllText((Join-Path $global:HyperlibLauncherTestFixture 'app/HyperLib.Host.exe'), 'synthetic old host')
    [IO.File]::WriteAllText((Join-Path $global:HyperlibLauncherTestFixture 'app/releases/review-v2/HyperLib.Host.exe'), 'synthetic installed host')
    if ($WithPointer) { [IO.File]::WriteAllText((Join-Path $global:HyperlibLauncherTestFixture 'app/host-target.json'), '{"relativeExecutable":"releases/review-v2/HyperLib.Host.exe"}') }
    $global:HyperlibLauncherTestLaunchCalls.Clear(); $global:HyperlibLauncherTestBuildCalls = 0; $global:HyperlibLauncherTestHealthCalls = 0
    $global:HyperlibLauncherTestStarted = $false; $global:HyperlibLauncherTestExistingHealth = $false
    $global:HyperlibLauncherTestHealthRoot = $global:HyperlibLauncherTestFixture
    $global:HyperlibLauncherTestListenerExecutable = Join-Path $global:HyperlibLauncherTestFixture 'app/releases/review-v2/HyperLib.Host.exe'
}
function Run-Launcher { & (Join-Path $global:HyperlibLauncherTestFixture 'Start-HyperLib.ps1') -Port 54189 }
function Reject-Launcher([string]$ExpectedMessage, [string]$Description) {
    $rejected = $false
    try { $null = Run-Launcher }
    catch { $rejected = $_.Exception.Message -like "*$ExpectedMessage*" }
    Check $rejected $Description
    Check ($global:HyperlibLauncherTestLaunchCalls.Count -eq 0 -and $global:HyperlibLauncherTestBuildCalls -eq 0) ($Description + ' without launching or rebuilding')
}

New-Fixture 'installed-launch'
$address = Run-Launcher
Check ($address -eq 'http://127.0.0.1:54189') 'Installed launch returns its local address'
Check ($global:HyperlibLauncherTestLaunchCalls.Count -eq 1 -and $global:HyperlibLauncherTestLaunchCalls[0].path -eq $global:HyperlibLauncherTestListenerExecutable -and $global:HyperlibLauncherTestLaunchCalls[0].path -like '*review-v2*') 'Authoritative pointer selects the new release instead of legacy executable'
Check ($global:HyperlibLauncherTestLaunchCalls[0].windowStyle -eq 'Hidden' -and $global:HyperlibLauncherTestBuildCalls -eq 0) 'Installed launch stays hidden and never rebuilds immutable output'

New-Fixture 'installed-reuse'
$global:HyperlibLauncherTestExistingHealth = $true
$null = Run-Launcher
Check ($global:HyperlibLauncherTestLaunchCalls.Count -eq 0) 'Matching installed executable and library are reused'

New-Fixture 'old-session'
$global:HyperlibLauncherTestExistingHealth = $true
$global:HyperlibLauncherTestListenerExecutable = Join-Path $global:HyperlibLauncherTestFixture 'app/HyperLib.Host.exe'
Reject-Launcher '-Port' 'Old same-library host is refused with an alternate-port instruction'

New-Fixture 'unknown-listener'
$global:HyperlibLauncherTestExistingHealth = $true; $global:HyperlibLauncherTestListenerExecutable = ''
Reject-Launcher 'unverified' 'Unreadable socket owner cannot be silently adopted'

New-Fixture 'other-library'
$global:HyperlibLauncherTestExistingHealth = $true; $global:HyperlibLauncherTestHealthRoot = Join-Path $evidence 'another-library'
Reject-Launcher 'different application or library' 'Same executable cannot adopt a different library'

$invalidPointers = [ordered]@{
    traversal = '{"relativeExecutable":"../HyperLib.Host.exe"}'
    rooted = '{"relativeExecutable":"C:/HyperLib.Host.exe"}'
    stream = '{"relativeExecutable":"releases/review-v2/HyperLib.Host.exe:other"}'
    wrongName = '{"relativeExecutable":"releases/review-v2/Other.exe"}'
    emptySegment = '{"relativeExecutable":"releases//HyperLib.Host.exe"}'
    trailingSpace = '{"relativeExecutable":"releases /HyperLib.Host.exe"}'
    wrongType = '{"relativeExecutable":42}'
    malformed = '{broken'
    missingTarget = '{"relativeExecutable":"releases/missing/HyperLib.Host.exe"}'
    oversized = (' ' * 16385)
}
foreach ($item in $invalidPointers.GetEnumerator()) {
    New-Fixture ('invalid-' + $item.Key)
    [IO.File]::WriteAllText((Join-Path $global:HyperlibLauncherTestFixture 'app/host-target.json'), $item.Value)
    $global:HyperlibLauncherTestExistingHealth = $true
    Reject-Launcher 'host target is invalid' ('Unsafe or unavailable pointer fails closed: ' + $item.Key)
    Check ($global:HyperlibLauncherTestHealthCalls -eq 0) ('Pointer validation precedes old-host adoption: ' + $item.Key)
}

New-Fixture 'linked-target'
$outside = Join-Path $evidence 'outside-target'
New-Item -ItemType Directory -Path $outside -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $outside 'HyperLib.Host.exe'), 'synthetic external host')
New-Item -ItemType Junction -Path (Join-Path $global:HyperlibLauncherTestFixture 'app/releases/linked') -Target $outside | Out-Null
[IO.File]::WriteAllText((Join-Path $global:HyperlibLauncherTestFixture 'app/host-target.json'), '{"relativeExecutable":"releases/linked/HyperLib.Host.exe"}')
Reject-Launcher 'Linked HyperLib host targets' 'Contained path through a reparse directory is rejected'

New-Fixture 'legacy-launch' $false
$null = Run-Launcher
Check ($global:HyperlibLauncherTestLaunchCalls.Count -eq 1 -and $global:HyperlibLauncherTestLaunchCalls[0].path -eq (Join-Path $global:HyperlibLauncherTestFixture 'app/HyperLib.Host.exe')) 'Absent pointer preserves legacy portable launch'

New-Fixture 'legacy-build' $false
$legacyExecutable = [IO.Path]::GetFullPath((Join-Path $global:HyperlibLauncherTestFixture 'app/HyperLib.Host.exe'))
Check ($legacyExecutable.StartsWith([IO.Path]::GetFullPath($evidence) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) 'Synthetic build target remains inside test evidence'
Remove-Item -LiteralPath $legacyExecutable
$null = Run-Launcher
Check ($global:HyperlibLauncherTestBuildCalls -eq 1 -and $global:HyperlibLauncherTestLaunchCalls.Count -eq 1) 'Absent pointer and missing executable preserve on-demand publish'

$receipt = [ordered]@{ status = 'HYPERLIB_START_LAUNCHER_OK'; checks = $checks.Count; source = $source; sourceSha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash; evidence = $evidence; checkNames = $checks.ToArray(); realProcessLaunches = 0 }
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'start-launcher-verification.json')
$receipt | ConvertTo-Json -Depth 5
