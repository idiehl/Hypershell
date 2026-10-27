#Requires -Version 7.0
Set-StrictMode -Version Latest

function Assert-TaskRegularPath([string]$Path) {
    if (-not [IO.Path]::IsPathFullyQualified($Path) -or $Path -match '["\r\n]') { throw 'An absolute fixed task path is required.' }
    $full = [IO.Path]::GetFullPath($Path)
    for ($part=$full; $part; $part=(Split-Path $part -Parent)) {
        if ((Test-Path -LiteralPath $part) -and ((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Task components must not traverse reparse points.' }
    }
    return $full
}

function Get-TaskLauncherPath {
    return Assert-TaskRegularPath ([IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'AtlasMeridian/McpCompanion/SecureTunnel/bin/TaskLauncher/Meridian.TaskLauncher.exe')))
}

function Get-FixedTaskPowerShell([ValidateSet('backend','secure-tunnel')][string]$Mode) {
    $profile = if ($Mode -eq 'backend') { 'Stable' } else { 'SecureTunnel' }
    return Assert-TaskRegularPath ([IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "AtlasMeridian/McpCompanion/$profile/bin/PowerShell7/pwsh.exe")))
}

function Assert-TaskLauncherInstallation {
    $launcher = Get-TaskLauncherPath
    $manifestPath = Assert-TaskRegularPath (Join-Path (Split-Path $launcher -Parent) 'build-manifest.json')
    if (-not (Test-Path -LiteralPath $launcher -PathType Leaf) -or -not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Install the verified no-console task launcher before installing or starting tasks.' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
    $repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
    if ($manifest.schema -ne 'atlas-meridian-task-launcher-v1' -or $manifest.sourceRoot -ne $repoRoot -or $manifest.backendPowerShell -ne (Get-FixedTaskPowerShell backend) -or $manifest.securePowerShell -ne (Get-FixedTaskPowerShell secure-tunnel) -or $manifest.launcherSha256 -notmatch '^[a-fA-F0-9]{64}$' -or $manifest.powerShellSha256 -notmatch '^[a-fA-F0-9]{64}$' -or $manifest.singleFile -ne $true -or $manifest.files.Count -ne 1 -or $manifest.files['Meridian.TaskLauncher.exe'] -ne $manifest.launcherSha256) { throw 'The task launcher manifest does not match this fixed installation.' }
    foreach ($sidecar in @('Meridian.TaskLauncher.dll','Meridian.TaskLauncher.runtimeconfig.json','Meridian.TaskLauncher.deps.json')) {
        if (Test-Path -LiteralPath (Join-Path (Split-Path $launcher -Parent) $sidecar)) { throw 'Unexpected loose managed sidecars found for the fixed single-file task launcher.' }
    }
    if ((Get-FileHash -LiteralPath $launcher -Algorithm SHA256).Hash -ne $manifest.launcherSha256) { throw 'The no-console task launcher hash changed.' }
    foreach ($mode in @('backend','secure-tunnel')) {
        $shell = Get-FixedTaskPowerShell $mode
        if (-not (Test-Path -LiteralPath $shell -PathType Leaf) -or (Get-FileHash -LiteralPath $shell -Algorithm SHA256).Hash -ne $manifest.powerShellSha256) { throw 'A pinned fixed PowerShell installation changed.' }
    }
    $stream = [IO.File]::OpenRead($launcher)
    try {
        $reader = [IO.BinaryReader]::new($stream)
        if ($reader.ReadUInt16() -ne 0x5A4D) { throw 'The task launcher is not a PE executable.' }
        $stream.Position=0x3c; $peOffset=$reader.ReadInt32(); $stream.Position=$peOffset
        if ($reader.ReadUInt32() -ne 0x4550) { throw 'The task launcher PE signature is invalid.' }
        $stream.Position=$peOffset+24+68
        if ($reader.ReadUInt16() -ne 2) { throw 'The task launcher must use Windows GUI subsystem; console launchers are rejected.' }
    } finally { $stream.Dispose() }
    return $launcher
}

function Get-BackendTaskIdentity {
    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    return @{name='Atlas Meridian Local Backend';path='\';userSid=$sid;description=('Atlas Meridian local backend lifecycle v2; no-console; owner '+$sid);scriptPath=(Join-Path $PSScriptRoot 'run-backend-task.ps1');launcherPath=(Get-TaskLauncherPath);arguments='backend';powershellPath=(Get-FixedTaskPowerShell backend);workingDirectory=[IO.Path]::GetFullPath($PSScriptRoot)}
}

function Assert-OwnedBackendTask($Task, [switch]$AllowLegacyUpgrade) {
    $identity=Get-BackendTaskIdentity
    $principalSid=$null
    if ($Task) { try { $principalSid=if($Task.Principal.UserId -match '^S-1-'){$Task.Principal.UserId}else{[Security.Principal.NTAccount]::new($Task.Principal.UserId).Translate([Security.Principal.SecurityIdentifier]).Value} } catch {} }
    if (-not $Task -or @($Task.Actions).Count -ne 1 -or $principalSid -ne $identity.userSid -or $Task.Principal.LogonType -ne 'Interactive' -or $Task.Principal.RunLevel -ne 'Limited') { throw 'The backend task is not the exact owned limited-user task.' }
    $action=$Task.Actions[0]
    if ([IO.Path]::GetFullPath($action.WorkingDirectory) -ne $identity.workingDirectory) { throw 'The backend task working directory changed.' }
    if ($Task.Description -eq $identity.description -and [IO.Path]::GetFullPath($action.Execute) -eq $identity.launcherPath -and $action.Arguments -eq $identity.arguments) { return $Task }
    if ($AllowLegacyUpgrade -and $Task.Description -eq 'Atlas Meridian local backend lifecycle v1; current-user installed application only' -and [IO.Path]::GetFullPath($action.Execute) -eq $identity.powershellPath -and $action.Arguments -eq ('-NoProfile -WindowStyle Hidden -File "'+$identity.scriptPath+'"')) { return $Task }
    throw 'The backend task action does not match the fixed owned launcher; it was not changed.'
}
