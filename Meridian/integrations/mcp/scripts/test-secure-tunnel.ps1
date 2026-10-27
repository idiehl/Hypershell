#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'secure-tunnel-common.ps1')
$passed = [Collections.Generic.List[string]]::new()
function Assert-Check([string]$Name, [bool]$Condition) {
    if (-not $Condition) { throw "Secure Tunnel test failed: $Name" }
    $passed.Add($Name)
}
function Assert-Rejected([string]$Name, [scriptblock]$Operation) {
    $rejected = $false
    try { & $Operation | Out-Null } catch { $rejected = $true }
    Assert-Check $Name $rejected
}
$fixtureParent = [IO.Path]::GetFullPath((Join-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent) 'output'))
[IO.Directory]::CreateDirectory($fixtureParent) | Out-Null
$fixture = Join-Path $fixtureParent ('secure-tunnel-tests-' + [Guid]::NewGuid().ToString('n'))
$junction = Join-Path $fixture 'junction'
try {
    $privateRoot = Initialize-SecureTunnelState (Join-Path $fixture 'private')
    Assert-Check 'Private state created and ACL verified' (Test-Path -LiteralPath (Join-Path $privateRoot '.meridian-secure-tunnel-state'))
    $unowned = Join-Path $fixture 'unowned'
    [IO.Directory]::CreateDirectory($unowned) | Out-Null
    [IO.File]::WriteAllText((Join-Path $unowned 'retained.txt'), 'Keep existing contents')
    Assert-Rejected 'Unrecognized state is not adopted' { Initialize-SecureTunnelState $unowned }
    $secretValue = 'sk-unit-test-' + [Guid]::NewGuid().ToString('n')
    $secret = ConvertTo-SecureString $secretValue -AsPlainText -Force
    Save-SecureTunnelKey $secret $privateRoot
    $secret.Dispose()
    $cipher = [IO.File]::ReadAllText((Join-Path $privateRoot 'runtime-key.dpapi'))
    Assert-Check 'Stored key is DPAPI ciphertext' (-not $cipher.Contains($secretValue) -and $cipher.Length -gt $secretValue.Length)
    foreach ($badKey in @(('tunnel_' + ('a'*32)), ('tracking-' + ('a'*32)), 'sk-invalid key with spaces', '"sk-quoted-key-not-allowed"')) {
        $invalid=ConvertTo-SecureString $badKey -AsPlainText -Force
        try { Assert-Rejected 'Non-key identifiers and malformed runtime keys rejected' { Save-SecureTunnelKey $invalid $privateRoot } }
        finally { $invalid.Dispose() }
    }
    Assert-Check 'Invalid key never replaces existing encrypted key' ([IO.File]::ReadAllText((Join-Path $privateRoot 'runtime-key.dpapi')) -eq $cipher)
    $restored = Get-SecureTunnelKey $privateRoot
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($restored)
    try { Assert-Check 'Current-user key round trip' ([Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) -eq $secretValue) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr); $restored.Dispose() }
    $missingRoot = Join-Path $fixture 'missing'
    Assert-Rejected 'Missing runtime key fails closed' { Get-SecureTunnelKey $missingRoot }
    $fakeBinary = Join-Path $fixture 'not-executable.txt'
    [IO.File]::WriteAllText($fakeBinary, 'Hash fixture; never execute')
    $base = @{
        transport='openai-secure-tunnel-stdio'; authoringEnabled=$true; authoringConsent=$true
        projectIds=@('47f9024f-2a26-4a9a-91b9-83927029fbf3'); meridianOrigin='http://127.0.0.1:5240'; stateRoot=$privateRoot
        tunnelId=('tunnel_' + ('a' * 32)); launcher=@{nodePath=(Get-Process -Id $PID).Path; serverPath=(Join-Path (Split-Path $PSScriptRoot -Parent) 'src/stdio.mjs'); tunnelPath=$fakeBinary; tunnelSha256=(Get-FileHash -LiteralPath $fakeBinary -Algorithm SHA256).Hash}
    }
    Assert-SecureTunnelConfig $base $privateRoot | Out-Null
    Assert-Check 'Fixed stdio profile validation' $true
    $withBrowser = $base.Clone(); $withBrowser.browserExecutablePath = $fakeBinary
    Assert-SecureTunnelConfig $withBrowser $privateRoot | Out-Null
    Assert-Check 'Optional trusted browser accepts an existing regular absolute file' $true
    $withBrowser.rulesPath = Join-Path $privateRoot 'rules.txt'
    $withBrowser.launcher = $base.launcher.Clone(); $withBrowser.launcher.retainedGuiLauncher = 'fixture-only'
    $reconfigured = Merge-SecureTunnelConfig $withBrowser @{ tunnelId=('tunnel_' + ('b' * 32)); launcher=@{tunnelSha256=$withBrowser.launcher.tunnelSha256} }
    Assert-Check 'Reconfiguration preserves browser executable and explicit project allowlist' ($reconfigured.browserExecutablePath -eq $fakeBinary -and @($reconfigured.projectIds).Count -eq 1 -and $reconfigured.projectIds[0] -eq $base.projectIds[0])
    Assert-Check 'Reconfiguration preserves rulebook and GUI launcher metadata' ($reconfigured.rulesPath -eq $withBrowser.rulesPath -and $reconfigured.launcher.retainedGuiLauncher -eq 'fixture-only')
    $reconfigured.launcher.retainedGuiLauncher = 'changed-copy'
    Assert-Check 'Configuration merge does not mutate the existing launcher object' ($withBrowser.launcher.retainedGuiLauncher -eq 'fixture-only')
    foreach ($browser in @('relative/chrome.exe', $privateRoot, (Join-Path $privateRoot 'missing-browser.exe'))) {
        $bad = $base.Clone(); $bad.browserExecutablePath = $browser
        Assert-Rejected 'Relative directory and absent browser executable paths rejected' { Assert-SecureTunnelConfig $bad $privateRoot }
    }
    foreach ($browser in @('\\unreachable.invalid\share\chrome.exe', '//unreachable.invalid/share/chrome.exe', '\\?\C:\chrome.exe', '\\.\C:\chrome.exe', 'https://unreachable.invalid/chrome.exe', ('C:\chrome' + [char]9 + '.exe'))) {
        $bad = $base.Clone(); $bad.browserExecutablePath = $browser
        Assert-Rejected 'Network device URL and control-character browser paths rejected locally' { Assert-SecureTunnelConfig $bad $privateRoot }
    }
    Assert-SecureTunnelBinary $base
    Assert-Check 'Matching binary hash accepted without execution' $true
    $bad = $base.Clone(); $bad.tunnelId='tunnel_short'
    Assert-Rejected 'Incomplete tunnel ID rejected' { Assert-SecureTunnelConfig $bad $privateRoot }
    $bad = $base.Clone(); $bad.authoringConsent=$false
    Assert-Rejected 'Missing authoring consent rejected' { Assert-SecureTunnelConfig $bad $privateRoot }
    $bad = $base.Clone(); $bad.projectIds=@()
    Assert-Rejected 'Empty project allowlist rejected' { Assert-SecureTunnelConfig $bad $privateRoot }
    foreach ($origin in @('http://localhost:5240','https://example.com','http://127.0.0.1:5240/path','http://user@127.0.0.1:5240')) {
        $bad = $base.Clone(); $bad.meridianOrigin=$origin
        Assert-Rejected ('Unsafe backend origin rejected: ' + $origin) { Assert-SecureTunnelConfig $bad $privateRoot }
    }
    $base.launcher.tunnelSha256 = '0' * 64
    Assert-Rejected 'Changed binary hash rejected before execution' { Assert-SecureTunnelBinary $base }
    $command = Get-SecureTunnelCommand $base $privateRoot
    Assert-Check 'Stdio command contains no key' (-not $command.Contains($secretValue) -and $command.Contains('stdio.mjs') -and $command.Contains('--config'))
    $summary = Get-SecureTunnelSummary @{exitCode=1;value=@{error='not ready'}}
    Assert-Check 'Malformed CLI result does not imply readiness' (-not $summary.processRunning -and -not $summary.ready)
    $target = Join-Path $fixture 'junction-target'
    [IO.Directory]::CreateDirectory($target) | Out-Null
    New-Item -ItemType Junction -Path $junction -Target $target | Out-Null
    Assert-Rejected 'Reparse-point state rejected' { Assert-SecureRegularPath (Join-Path $junction 'state.json') }
    Assert-Rejected 'Unknown scheduled task not adopted' { Assert-OwnedSecureTunnelTask $null }
    $taskIdentity = Get-SecureTunnelTaskIdentity
    $taskArguments = $taskIdentity.arguments
    $mockTask = [pscustomobject]@{ Description=$taskIdentity.description; Actions=@([pscustomobject]@{Arguments=$taskArguments;Execute=$taskIdentity.launcherPath;WorkingDirectory=$taskIdentity.workingDirectory}); Principal=[pscustomobject]@{UserId=$taskIdentity.userSid;LogonType='Interactive';RunLevel='Limited'} }
    Assert-OwnedSecureTunnelTask $mockTask | Out-Null
    Assert-Check 'Exact trusted scheduled action accepted' $true
    $mockTask.Actions[0].Execute = 'C:\Untrusted\pwsh.exe'
    Assert-Rejected 'Same basename at untrusted location rejected' { Assert-OwnedSecureTunnelTask $mockTask }
    $mockTask.Actions[0].Execute = $taskIdentity.launcherPath
    $mockTask.Actions[0].WorkingDirectory = 'C:\Untrusted'
    Assert-Rejected 'Different task working directory rejected' { Assert-OwnedSecureTunnelTask $mockTask }
    $mockTask.Actions[0].WorkingDirectory = $taskIdentity.workingDirectory
    $mockTask.Actions[0].Arguments = 'secure-tunnel -Command calc'
    Assert-Rejected 'Additional Secure Tunnel arguments rejected' { Assert-OwnedSecureTunnelTask $mockTask }
    $mockTask.Actions[0].Arguments = $taskArguments
    $mockTask.Principal.RunLevel = 'Highest'
    Assert-Rejected 'Unexpected elevated Secure Tunnel principal rejected' { Assert-OwnedSecureTunnelTask $mockTask }
    [pscustomobject]@{ passed=$passed.Count; checks=$passed; liveStateTouched=$false; networkCalls=$false; secretsPrinted=$false } | ConvertTo-Json -Depth 5
} finally {
    # Check the exact absolute fixture boundary before recursive cleanup.
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if ((Split-Path $resolvedFixture -Parent) -ne $fixtureParent -or (Split-Path $resolvedFixture -Leaf) -notmatch '^secure-tunnel-tests-[a-f0-9]{32}$') { throw 'Unsafe fixture cleanup path.' }
    if (Test-Path -LiteralPath $junction) { Remove-Item -LiteralPath $junction -Force }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
