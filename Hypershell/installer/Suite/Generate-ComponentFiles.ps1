[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Authoring', 'Meridian')][string] $ComponentName,
    [Parameter(Mandatory)][string] $PublishDirectory,
    [Parameter(Mandatory)][string] $OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$publishRoot = [IO.Path]::GetFullPath($PublishDirectory)
if (-not [IO.Directory]::Exists($publishRoot)) { throw "Missing staged component: $publishRoot" }
for ($ancestor = $publishRoot; $ancestor; $ancestor = [IO.Path]::GetDirectoryName($ancestor)) {
    if (([IO.Directory]::Exists($ancestor) -or [IO.File]::Exists($ancestor)) -and
        ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'A component staging directory cannot contain a symbolic link or junction in its path.'
    }
}
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
$rootPrefix = $publishRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if ($resolvedOutput.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Write the generated WiX fragment outside the staged component.'
}

# These are never product payloads. A curated library seed may contain Works,
# Content, and Media, so release staging owns the explicit seed allowlist.
$blockedNames = @('.git', '.svn', '.hg', '.cache', '.hyperlib', '.visual-page',
    '.hypershell', 'node_modules', 'obj', 'secrets.json', 'appsettings.Local.json',
    'trust.device.json', 'apps.json', 'launch-target.json', '.env')
$files = [Collections.Generic.List[IO.FileInfo]]::new()
function Read-StagedDirectory([string] $Path) {
    foreach ($item in @(Get-ChildItem -LiteralPath $Path -Force | Sort-Object Name)) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Symbolic links and junctions are not accepted in release payloads: $($item.FullName)"
        }
        if ($item.Name -in $blockedNames -or $item.Name.StartsWith('.env.') -or
            $item.Extension -in @('.pfx', '.p12', '.pem', '.key', '.log', '.bak', '.tmp') -or
            ($item.Attributes -band [IO.FileAttributes]::Hidden) -ne 0) {
            throw "Development state, private data, or secrets are not release payloads: $($item.FullName)"
        }
        if ($item.PSIsContainer) { Read-StagedDirectory $item.FullName }
        else { $files.Add($item) }
    }
}
Read-StagedDirectory $publishRoot
if ($files.Count -eq 0) { throw 'A component payload cannot be empty.' }
$required = if ($ComponentName -eq 'Authoring') {
    @('studio/Hypershell.exe', 'studio/Hypershell.dll', 'Hyperlib/hyperlib.app.json', 'Hyperlib/app/HyperLib.Host.exe',
        'Hyperlib/Starters/standard/src/Host/Host.csproj', 'Hyperlib/Starters/standard/src/Pages/Pages.csproj')
} else { @('Meridian.Host.exe', 'Meridian.Host.dll', 'wwwroot/index.html') }
foreach ($relative in $required) {
    if (-not [IO.File]::Exists((Join-Path $publishRoot $relative))) {
        throw "The $ComponentName payload is incomplete: $relative"
    }
}

function Get-StableId([string] $Prefix, [string] $Value) {
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes(($ComponentName + '/' + $Value).ToLowerInvariant()))).ToLowerInvariant()
    return "${Prefix}_$($hash.Substring(0, 24))"
}
function Escape-Xml([string] $Value) { [Security.SecurityElement]::Escape($Value) }
$lines = [Collections.Generic.List[string]]::new()
$ids = [Collections.Generic.List[string]]::new()
$lines.Add('<?xml version="1.0" encoding="utf-8"?>')
$lines.Add('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
$lines.Add('  <Fragment>')
$lines.Add('    <DirectoryRef Id="COMPONENTFOLDER">')
function Add-StagedDirectory([string] $Path, [string] $Relative, [int] $Indent) {
    $padding = ' ' * $Indent
    foreach ($file in @($files | Where-Object { $_.DirectoryName -eq $Path } | Sort-Object Name)) {
        $relativeFile = if ($Relative) { "$Relative/$($file.Name)" } else { $file.Name }
        $componentId = Get-StableId 'cmp' $relativeFile
        $fileId = Get-StableId 'fil' $relativeFile
        $ids.Add($componentId)
        $lines.Add("$padding<Component Id=`"$componentId`" Guid=`"*`">")
        $lines.Add("$padding  <File Id=`"$fileId`" Source=`"$(Escape-Xml $file.FullName)`" KeyPath=`"yes`" />")
        $lines.Add("$padding</Component>")
    }
    foreach ($directory in @(Get-ChildItem -LiteralPath $Path -Directory -Force | Sort-Object Name)) {
        $relativeDirectory = if ($Relative) { "$Relative/$($directory.Name)" } else { $directory.Name }
        $directoryId = Get-StableId 'dir' $relativeDirectory
        $lines.Add("$padding<Directory Id=`"$directoryId`" Name=`"$(Escape-Xml $directory.Name)`">")
        Add-StagedDirectory $directory.FullName $relativeDirectory ($Indent + 2)
        $lines.Add("$padding</Directory>")
    }
}
Add-StagedDirectory $publishRoot '' 6
$lines.Add('    </DirectoryRef>')
$lines.Add('  </Fragment>')
$lines.Add('  <Fragment>')
$lines.Add('    <ComponentGroup Id="ComponentFiles">')
foreach ($id in $ids) { $lines.Add("      <ComponentRef Id=`"$id`" />") }
$lines.Add('    </ComponentGroup>')
$lines.Add('  </Fragment>')
$lines.Add('</Wix>')
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
[IO.File]::WriteAllLines($resolvedOutput, $lines, [Text.UTF8Encoding]::new($false))
Write-Output "HYPERSHELL_COMPONENT_FILES_OK|$ComponentName|$($files.Count)|$resolvedOutput"
