#requires -Version 7.2
<#
.SYNOPSIS
Stores an Atlas Archives publisher credential for this Windows user and library.
.DESCRIPTION
The key is encrypted with Windows DPAPI outside the portable library. It is
never exposed to Hyperlib page scripts or included in publication packages.
The Archives administrator must install the matching SHA-256 on the receiver.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $HyperlibRoot,
    [Parameter(Mandatory)] [Security.SecureString] $PublisherKey
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'The desktop Archives connection requires Windows.' }
$libraryPath = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($HyperlibRoot))
if (-not (Test-Path -LiteralPath (Join-Path $libraryPath 'hyperlib.app.json') -PathType Leaf)) { throw 'Choose an existing Hyperlib root.' }
$identityBytes = [Text.Encoding]::UTF8.GetBytes($libraryPath.ToUpperInvariant())
$identity = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($identityBytes)).ToLowerInvariant()
$settingsRoot = if ([string]::IsNullOrWhiteSpace($env:HYPERSHELL_APP_DATA)) { Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Hypershell' } else { [IO.Path]::GetFullPath($env:HYPERSHELL_APP_DATA) }
$directory = Join-Path $settingsRoot "Archives/$identity"
$path = Join-Path $directory 'connection.dpapi'
$credential = [Net.NetworkCredential]::new('', $PublisherKey)
$token = $credential.Password
try {
    if ($token.Length -lt 40 -or $token.Length -gt 512 -or $token -match '\s') { throw 'Invalid publisher credential.' }
    $payload = [Text.Encoding]::UTF8.GetBytes((@{ origin = 'https://atlasuniversalis.com'; token = $token } | ConvertTo-Json -Compress))
    $entropy = [Text.Encoding]::UTF8.GetBytes('Hypershell.Hyperlib.Archives.v1')
    try { $encrypted = [Security.Cryptography.ProtectedData]::Protect($payload, $entropy, [Security.Cryptography.DataProtectionScope]::CurrentUser) }
    finally { [Security.Cryptography.CryptographicOperations]::ZeroMemory($payload) }
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporary = "$path.$([Guid]::NewGuid().ToString('N')).tmp"
    [IO.File]::WriteAllBytes($temporary, $encrypted)
    [IO.File]::Move($temporary, $path, $true)
    Write-Output 'Atlas Archives connection saved for this Windows user and Hyperlib library.'
}
finally { $token = $null; $credential = $null }
