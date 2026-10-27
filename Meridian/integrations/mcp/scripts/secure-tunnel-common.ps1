#Requires -Version 7.0
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'task-launcher-common.ps1')

function Get-SecureTunnelRoot {
    return [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'AtlasMeridian/McpCompanion/SecureTunnel'))
}

function Assert-SecureRegularPath([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $current = $resolved
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Secure Tunnel state and executables must not traverse reparse points.' }
        $parent = Split-Path $current -Parent
        if ($parent -eq $current) { break }
        $current = $parent
    }
    return $resolved
}

function Initialize-SecureTunnelState([string]$Root = (Get-SecureTunnelRoot)) {
    $Root = Assert-SecureRegularPath $Root
    $marker = Join-Path $Root '.meridian-secure-tunnel-state'
    if ((Test-Path -LiteralPath $Root) -and -not (Test-Path -LiteralPath $marker) -and @(Get-ChildItem -LiteralPath $Root -Force).Count) { throw 'The Secure Tunnel state directory has unrecognized contents; permissions were not changed.' }
    [IO.Directory]::CreateDirectory($Root) | Out-Null
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $system = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $acl.SetOwner($sid)
    foreach ($principal in @($sid, $system)) {
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($principal, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
    }
    [IO.FileSystemAclExtensions]::SetAccessControl([IO.DirectoryInfo]::new($Root), $acl)
    $actual = [IO.FileSystemAclExtensions]::GetAccessControl([IO.DirectoryInfo]::new($Root))
    $rules = @($actual.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
    if (-not $actual.AreAccessRulesProtected -or $actual.GetOwner([Security.Principal.SecurityIdentifier]).Value -ne $sid.Value -or $rules.Count -ne 2) { throw 'Secure Tunnel directory privacy could not be verified.' }
    foreach ($rule in $rules) {
        if ($rule.IdentityReference.Value -notin @($sid.Value, $system.Value) -or $rule.AccessControlType -ne 'Allow' -or $rule.FileSystemRights -ne 'FullControl') { throw 'Secure Tunnel directory privacy could not be verified.' }
    }
    [IO.File]::WriteAllText($marker, 'atlas-meridian-secure-tunnel-v1')
    return $Root
}

function Write-SecureTunnelJson([string]$Path, $Value) {
    Assert-SecureRegularPath $Path | Out-Null
    $temporary = "$Path.new"
    Assert-SecureRegularPath $temporary | Out-Null
    [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $Path -Force
}

function Merge-SecureTunnelConfig([System.Collections.IDictionary]$Existing, [System.Collections.IDictionary]$Updates) {
    # This profile contains routing/scope metadata; its API key is a separate
    # DPAPI file. Keep existing browser, rulebook and GUI-launcher configuration
    # when an operator updates credentials or the tunnel connection.
    $merged = if ($Existing) { $Existing | ConvertTo-Json -Depth 30 | ConvertFrom-Json -AsHashtable } else { @{} }
    foreach ($name in $Updates.Keys) {
        if ($name -eq 'launcher' -and $merged.ContainsKey('launcher')) {
            foreach ($field in $Updates.launcher.Keys) { $merged.launcher[$field] = $Updates.launcher[$field] }
        } else { $merged[$name] = $Updates[$name] }
    }
    return $merged
}

function Assert-SecureTunnelConfig($Config, [string]$Root = (Get-SecureTunnelRoot)) {
    if ($Config.transport -ne 'openai-secure-tunnel-stdio' -or $Config.authoringEnabled -ne $true -or $Config.authoringConsent -ne $true) { throw 'Explicit local consent for the Secure Tunnel stdio authoring transport is required.' }
    if ($Config.tunnelId -notmatch '^tunnel_(?:[a-z0-9]{4}_)?[a-z0-9]{32}$') { throw 'Preserve the complete valid tunnel ID returned by OpenAI.' }
    if ([IO.Path]::GetFullPath($Config.stateRoot) -ne [IO.Path]::GetFullPath($Root)) { throw 'Secure Tunnel config must use its own private persistent state root.' }
    $origin = [Uri]$Config.meridianOrigin
    if (-not $origin.IsAbsoluteUri -or $origin.Scheme -ne 'http' -or $origin.Host -notin @('127.0.0.1','[::1]') -or $origin.UserInfo -or $origin.AbsolutePath -ne '/' -or $origin.Query -or $origin.Fragment) { throw 'Meridian must use an explicit numeric loopback HTTP origin.' }
    if (@($Config.projectIds).Count -lt 1 -or @($Config.projectIds).Count -gt 100) { throw 'An explicit project allowlist is required.' }
    foreach ($id in $Config.projectIds) { if ($id -notmatch '^[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$') { throw 'The project allowlist contains an invalid ID.' } }
    foreach ($field in @('nodePath','serverPath','tunnelPath')) {
        $value = [string]$Config.launcher[$field]
        if (-not [IO.Path]::IsPathFullyQualified($value) -or $value -match '["\r\n]') { throw 'Secure Tunnel executable and server paths must be absolute paths without quotes or newlines.' }
        Assert-SecureRegularPath $value | Out-Null
        if (-not (Test-Path -LiteralPath $value -PathType Leaf)) { throw 'A configured Secure Tunnel executable or server file is missing.' }
    }
    if ($Config.ContainsKey('browserExecutablePath')) {
        $browser = [string]$Config.browserExecutablePath
        # Reject network/device paths before any filesystem lookup; preview
        # configuration may identify only a local owner-selected executable.
        if (-not [IO.Path]::IsPathFullyQualified($browser) -or $browser.StartsWith('\\') -or $browser.StartsWith('//') -or $browser -match '^[A-Za-z][A-Za-z0-9+.-]*://' -or $browser -match '["\x00-\x1F\x7F]') { throw 'The trusted preview browser path must be an absolute local path without quotes or control characters.' }
        Assert-SecureRegularPath $browser | Out-Null
        if (-not (Test-Path -LiteralPath $browser -PathType Leaf)) { throw 'The trusted preview browser executable is missing or is not a regular file.' }
    }
    $fixedServer = [IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) 'src/stdio.mjs'))
    if ([IO.Path]::GetFullPath($Config.launcher.serverPath) -ne $fixedServer) { throw 'The Secure Tunnel profile must run this fixed Meridian stdio entry point.' }
    if ($Config.launcher.tunnelSha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'A verified SHA-256 pin is required for the OpenAI tunnel-client.' }
    return $Config
}

function Get-SecureTunnelConfig {
    $root = Get-SecureTunnelRoot
    $path = Join-Path $root 'config.json'
    Assert-SecureRegularPath $path | Out-Null
    if (-not (Test-Path -LiteralPath $path)) { throw 'Run configure-secure-tunnel.ps1 before starting Secure Tunnel.' }
    return Assert-SecureTunnelConfig (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable) $root
}

function Assert-SecureTunnelBinary($Config) {
    Assert-SecureRegularPath $Config.launcher.tunnelPath | Out-Null
    $actual = (Get-FileHash -LiteralPath $Config.launcher.tunnelPath -Algorithm SHA256).Hash
    if ($actual -ne $Config.launcher.tunnelSha256) { throw 'The OpenAI tunnel-client binary does not match its verified SHA-256; it was not executed.' }
}

function Save-SecureTunnelKey([Security.SecureString]$Key, [string]$Root = (Get-SecureTunnelRoot)) {
    # Validate the shape without creating a managed plaintext copy or exposing values.
    $valid = $Key.Length -ge 20
    $keyPointer = [IntPtr]::Zero
    try {
        $keyPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Key)
        for ($index=0; $index -lt $Key.Length; $index++) {
            $character = [char][Runtime.InteropServices.Marshal]::ReadInt16($keyPointer,($index*2))
            if ([char]::IsWhiteSpace($character) -or $character -in @([char]34,[char]39)) { $valid=$false }
            if ($index -lt 3 -and $character -cne 'sk-'[$index]) { $valid=$false }
        }
    } finally { if ($keyPointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($keyPointer) } }
    if (-not $valid) { throw 'Paste the full restricted OpenAI secret key beginning with sk-, without quotes or whitespace; do not use the Tunnel ID, name, or tracking ID.' }
    $rootChecked = Initialize-SecureTunnelState $Root
    $path = Join-Path $rootChecked 'runtime-key.dpapi'
    Assert-SecureRegularPath $path | Out-Null
    # Windows DPAPI CurrentUser encryption; never a literal key or machine-wide environment variable.
    $protected = ConvertFrom-SecureString -SecureString $Key
    [IO.File]::WriteAllText($path, $protected, [Text.UTF8Encoding]::new($false))
}

function Get-SecureTunnelKey([string]$Root = (Get-SecureTunnelRoot)) {
    $path = Join-Path $Root 'runtime-key.dpapi'
    Assert-SecureRegularPath $path | Out-Null
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Enter the runtime API key locally with configure-secure-tunnel.ps1.' }
    return ConvertTo-SecureString ([IO.File]::ReadAllText($path))
}

function Get-SecureTunnelCommand($Config, [string]$Root = (Get-SecureTunnelRoot)) {
    # Forward slashes avoid shell-word parser ambiguity in Windows paths.
    $parts = @($Config.launcher.nodePath, $Config.launcher.serverPath, '--config', (Join-Path $Root 'config.json'))
    return (($parts | ForEach-Object { '"' + ([string]$_).Replace('\','/') + '"' }) -join ' ')
}

function Invoke-SecureTunnelClient($Config, [string[]]$Arguments, [int]$TimeoutSeconds = 60, [string]$Root = (Get-SecureTunnelRoot)) {
    Assert-SecureTunnelBinary $Config
    $secret = Get-SecureTunnelKey $Root
    $ptr = [IntPtr]::Zero
    $process = $null
    try {
        $info = [Diagnostics.ProcessStartInfo]::new()
        $info.FileName = $Config.launcher.tunnelPath
        $info.UseShellExecute = $false
        $info.CreateNoWindow = $true
        $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        $info.WorkingDirectory = $Root
        foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
        # Profiles and known defaults are the sole routing authority. Do not inherit
        # a different tunnel target, embedded MCP binding, token, or Node preload.
        foreach ($name in @($info.Environment.Keys)) {
            if ($name -match '^(CONTROL_PLANE_|MCP_|TUNNEL_CLIENT_|HARPOON_|HEALTH_|TRACING_|CLOUDFLARED_|ADMIN_UI_)' -or $name -in @('OPENAI_API_KEY','OPENAI_ADMIN_KEY','NODE_OPTIONS','NODE_PATH','PID_FILE')) { $info.Environment.Remove($name) | Out-Null }
        }
        $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
        $info.Environment['CONTROL_PLANE_API_KEY'] = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
        $info.Environment['CONTROL_PLANE_TUNNEL_ID'] = $Config.tunnelId
        $info.Environment['CONTROL_PLANE_BASE_URL'] = 'https://api.openai.com'
        $info.Environment['TUNNEL_CLIENT_STATE_DIR'] = Join-Path $Root 'runtime'
        $info.Environment['TUNNEL_CLIENT_PROFILE_DIR'] = Join-Path $Root 'profiles'
        $info.Environment['CLOUDFLARED_MANAGED'] = 'false'
        $info.Environment['TRACING_ENABLED'] = 'false'
        $process = [Diagnostics.Process]::Start($info)
        $info.Environment.Remove('CONTROL_PLANE_API_KEY') | Out-Null
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { $process.Kill(); $process.WaitForExit(); throw 'The tunnel-client operation timed out; inspect private local runtime diagnostics.' }
        $output = $stdout.GetAwaiter().GetResult()
        $null = $stderr.GetAwaiter().GetResult()
        # Never echo arbitrary CLI errors or captured output: network errors can contain sensitive metadata.
        $value = $null
        if ($output.TrimStart().StartsWith('{')) { try { $value = $output | ConvertFrom-Json -AsHashtable } catch { } }
        return @{ exitCode = $process.ExitCode; value = $value }
    } finally {
        if ($ptr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
        if ($secret) { $secret.Dispose() }
        if ($process) { $process.Dispose() }
    }
}

function Get-SecureTunnelTaskIdentity {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return @{ name='Atlas Meridian MCP Secure Tunnel'; path='\'; userSid=$identity.User.Value; description=('Atlas Meridian OpenAI Secure MCP Tunnel; owner ' + $identity.User.Value); scriptPath=(Join-Path $PSScriptRoot 'run-secure-tunnel-task.ps1'); powershellPath=(Get-FixedTaskPowerShell secure-tunnel); launcherPath=(Get-TaskLauncherPath); arguments='secure-tunnel'; workingDirectory=[IO.Path]::GetFullPath($PSScriptRoot) }
}

function Assert-OwnedSecureTunnelTask($Task) {
    $identity = Get-SecureTunnelTaskIdentity
    $expected = $identity.arguments
    $principalSid = $null
    if ($Task) { try { $principalSid = if ($Task.Principal.UserId -match '^S-1-') { $Task.Principal.UserId } else { [Security.Principal.NTAccount]::new($Task.Principal.UserId).Translate([Security.Principal.SecurityIdentifier]).Value } } catch { } }
    if (-not $Task -or $Task.Description -ne $identity.description -or @($Task.Actions).Count -ne 1 -or $Task.Actions[0].Arguments -ne $expected -or [IO.Path]::GetFullPath($Task.Actions[0].Execute) -ne $identity.launcherPath -or [IO.Path]::GetFullPath($Task.Actions[0].WorkingDirectory) -ne $identity.workingDirectory -or $principalSid -ne $identity.userSid -or $Task.Principal.LogonType -ne 'Interactive' -or $Task.Principal.RunLevel -ne 'Limited') { throw 'The existing task is not the exact owned Secure Tunnel task; it was not changed.' }
    return $Task
}

function Get-SecureTunnelSummary($Result) {
    if (-not $Result.value) { return @{ processRunning=$false; healthy=$false; ready=$false; runtimeState='unknown'; cliExitCode=$Result.exitCode } }
    $data = $Result.value
    return @{ processRunning=($data['process_running'] -eq $true); healthy=($data['healthy'] -eq $true); ready=($data['ready'] -eq $true); runtimeState=$data['runtime_state']; cliExitCode=$Result.exitCode; healthUrl=$data['health_url']; uiUrl=$data['ui_url'] }
}
