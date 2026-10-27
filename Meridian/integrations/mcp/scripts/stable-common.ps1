#Requires -Version 7.0
Set-StrictMode -Version Latest

function Get-StableStateRoot {
    $root = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'AtlasMeridian/McpCompanion/Stable'))
    foreach ($path in @($root, (Split-Path $root -Parent), (Split-Path (Split-Path $root -Parent) -Parent))) {
        if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Companion state must not traverse a junction or symbolic link.' }
    }
    return $root
}

function Initialize-StableState {
    $root = Get-StableStateRoot
    $marker = Join-Path $root '.meridian-mcp-stable-state'
    if ((Test-Path -LiteralPath $root) -and -not (Test-Path -LiteralPath $marker) -and @(Get-ChildItem -LiteralPath $root -Force).Count) { throw 'The stable state directory contains unrecognized files. No permissions changed.' }
    [IO.Directory]::CreateDirectory($root) | Out-Null
    $inheritance = [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'
    $propagation = [Security.AccessControl.PropagationFlags]::None
    $rights = [Security.AccessControl.FileSystemRights]::FullControl
    $allow = [Security.AccessControl.AccessControlType]::Allow
    $owner = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $system = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
    $info = [IO.DirectoryInfo]::new($root)
    $sections = [Security.AccessControl.AccessControlSections]::Access -bor [Security.AccessControl.AccessControlSections]::Owner
    function Test-PrivateAcl($Value) {
        if (-not $Value.AreAccessRulesProtected -or $Value.GetOwner([Security.Principal.SecurityIdentifier]).Value -ne $owner.Value) { return $false }
        $rules = @($Value.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
        if ($rules.Count -ne 2) { return $false }
        foreach ($rule in $rules) {
            if ($rule.IdentityReference.Value -notin @($owner.Value, $system.Value) -or $rule.AccessControlType -ne $allow -or $rule.FileSystemRights -ne $rights -or $rule.InheritanceFlags -ne $inheritance -or $rule.PropagationFlags -ne $propagation -or $rule.IsInherited) { return $false }
        }
        return (@($rules.IdentityReference.Value | Sort-Object -Unique).Count -eq 2)
    }
    $existing = [IO.FileSystemAclExtensions]::GetAccessControl($info, $sections)
    if (-not (Test-PrivateAcl $existing)) {
        $acl = [Security.AccessControl.DirectorySecurity]::new()
        $acl.SetAccessRuleProtection($true, $false)
        $acl.SetOwner($owner)
        foreach ($principal in @($owner, $system)) {
            $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($principal, $rights, $inheritance, $propagation, $allow))
        }
        # Persist only modified Access/Owner sections through .NET. Set-Acl can
        # request SACL privileges even though this setup never reads/writes audit rules.
        [IO.FileSystemAclExtensions]::SetAccessControl($info, $acl)
    }
    $verified = [IO.FileSystemAclExtensions]::GetAccessControl($info, $sections)
    if (-not (Test-PrivateAcl $verified)) { throw 'The private companion directory permissions could not be verified.' }
    Set-Content -LiteralPath $marker -Value 'atlas-meridian-stable-mcp-v1' -Encoding ascii
    return $root
}

function Write-StableJson([string]$Path, $Value) {
    $temporary = "$Path.new"
    $Value | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $temporary -Encoding utf8NoBOM
    Move-Item -LiteralPath $temporary -Destination $Path -Force
}

function Get-OwnedProcess($Identity) {
    if (-not $Identity -or -not $Identity.id) { return $null }
    $process = Get-Process -Id ([int]$Identity.id) -ErrorAction SilentlyContinue
    if (-not $process) { return $null }
    if ($process.Path -ne $Identity.path -or [Math]::Abs(($process.StartTime.ToUniversalTime() - ([DateTimeOffset]$Identity.startedAt).UtcDateTime).TotalMilliseconds) -gt 10) { throw 'A recorded helper process identity changed; it will not be stopped or adopted.' }
    return $process
}

function Get-ProcessIdentity($Process) {
    return @{ id = $Process.Id; path = $Process.Path; startedAt = $Process.StartTime.ToUniversalTime().ToString('o') }
}

function Assert-StableOrigin([string]$Origin) {
    $url = [Uri]$Origin
    if (-not $url.IsAbsoluteUri -or $url.Scheme -ne 'https' -or $url.UserInfo -or $url.AbsolutePath -ne '/' -or $url.Query -or $url.Fragment -or $url.Host.EndsWith('.trycloudflare.com')) { throw 'A stable HTTPS origin without credentials, path, query or fragment is required. Quick Tunnel hostnames are temporary.' }
    return $url.GetLeftPart([UriPartial]::Authority).TrimEnd('/')
}

function Get-StableConfig {
    $root = Get-StableStateRoot
    $path = Join-Path $root 'config.json'
    if (-not (Test-Path -LiteralPath $path)) { throw 'Configure a stable HTTPS endpoint with configure-stable.ps1 first.' }
    $config = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
    $config.publicOrigin = Assert-StableOrigin $config.publicOrigin
    if ($config.port -lt 1024 -or $config.port -gt 65535) { throw 'Invalid companion port.' }
    return $config
}

function Assert-PinnedTunnelBinary([string]$Path, [string]$Sha256) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf) -or $Sha256 -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Sha256) { throw 'The tunnel executable does not match its explicitly configured SHA-256. It has not been executed.' }
}

function Get-StableTaskIdentity {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return @{ name = 'Atlas Meridian MCP Stable'; path = '\'; userSid = $identity.User.Value; userName = $identity.Name; description = ('Atlas Meridian stable MCP supervisor; owner ' + $identity.User.Value); scriptPath = (Join-Path $PSScriptRoot 'run-stable-task.ps1') }
}

function Assert-OwnedStableTask($Task) {
    $identity = Get-StableTaskIdentity
    $expectedArguments = '-NoProfile -NonInteractive -WindowStyle Hidden -File "' + $identity.scriptPath + '"'
    $principalSid = $null
    if ($Task) {
        try {
            if ($Task.Principal.UserId -match '^S-1-') { $principalSid = [Security.Principal.SecurityIdentifier]::new($Task.Principal.UserId).Value }
            else { $principalSid = [Security.Principal.NTAccount]::new($Task.Principal.UserId).Translate([Security.Principal.SecurityIdentifier]).Value }
        } catch { }
    }
    if (-not $Task -or $Task.Description -ne $identity.description -or @($Task.Actions).Count -ne 1 -or $Task.Actions[0].Arguments -ne $expectedArguments -or [IO.Path]::GetFileName($Task.Actions[0].Execute) -ne 'pwsh.exe' -or $principalSid -ne $identity.userSid) { throw 'The scheduled task does not match this user and the fixed Meridian MCP action. It will not be started, changed, or removed.' }
    return $Task
}
