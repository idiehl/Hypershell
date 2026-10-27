[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PublishDirectory,

    [Parameter(Mandatory)]
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$publishRoot = [IO.Path]::GetFullPath($PublishDirectory)
if (-not [IO.Directory]::Exists($publishRoot)) {
    throw "The publish directory does not exist: $publishRoot"
}

function Get-StableId {
    param(
        [Parameter(Mandatory)]
        [string] $Prefix,

        [Parameter(Mandatory)]
        [string] $Value
    )

    $bytes = [Text.Encoding]::UTF8.GetBytes($Value.ToLowerInvariant())
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    return "${Prefix}_$($hash.Substring(0, 24))"
}

function Escape-Xml {
    param([Parameter(Mandatory)][string] $Value)
    return [Security.SecurityElement]::Escape($Value)
}

function Add-DirectoryXml {
    param(
        [Parameter(Mandatory)]
        [string] $DirectoryPath,

        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $RelativePath,

        [Parameter(Mandatory)]
        [Collections.Generic.List[string]] $Lines,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [Collections.Generic.List[string]] $ComponentIds,

        [Parameter(Mandatory)]
        [int] $Indent
    )

    $padding = ' ' * $Indent
    foreach ($file in @(Get-ChildItem -LiteralPath $DirectoryPath -File | Sort-Object Name)) {
        $relativeFile = if ([string]::IsNullOrEmpty($RelativePath)) {
            $file.Name
        }
        else {
            "$RelativePath/$($file.Name)"
        }
        $componentId = Get-StableId -Prefix 'cmp' -Value $relativeFile
        $fileId = Get-StableId -Prefix 'fil' -Value $relativeFile
        $ComponentIds.Add($componentId)
        $Lines.Add("$padding<Component Id=`"$componentId`" Guid=`"*`">")
        if ($relativeFile -ieq 'Hypershell.exe') {
            $Lines.Add("$padding  <File Id=`"$fileId`" Source=`"$(Escape-Xml $file.FullName)`" KeyPath=`"yes`">")
            $Lines.Add("$padding    <Shortcut Id=`"HypershellStartMenuShortcut`" Directory=`"ApplicationProgramsFolder`" Name=`"Hypershell`" WorkingDirectory=`"INSTALLFOLDER`" Advertise=`"yes`" />")
            $Lines.Add("$padding  </File>")
            $Lines.Add("$padding  <RemoveFolder Id=`"RemoveHypershellStartMenuFolder`" Directory=`"ApplicationProgramsFolder`" On=`"uninstall`" />")
        }
        else {
            $Lines.Add("$padding  <File Id=`"$fileId`" Source=`"$(Escape-Xml $file.FullName)`" KeyPath=`"yes`" />")
        }
        $Lines.Add("$padding</Component>")
    }

    foreach ($directory in @(Get-ChildItem -LiteralPath $DirectoryPath -Directory | Sort-Object Name)) {
        $relativeDirectory = if ([string]::IsNullOrEmpty($RelativePath)) {
            $directory.Name
        }
        else {
            "$RelativePath/$($directory.Name)"
        }
        $directoryId = Get-StableId -Prefix 'dir' -Value $relativeDirectory
        $Lines.Add("$padding<Directory Id=`"$directoryId`" Name=`"$(Escape-Xml $directory.Name)`">")
        Add-DirectoryXml `
            -DirectoryPath $directory.FullName `
            -RelativePath $relativeDirectory `
            -Lines $Lines `
            -ComponentIds $ComponentIds `
            -Indent ($Indent + 2)
        $Lines.Add("$padding</Directory>")
    }
}

$lines = [Collections.Generic.List[string]]::new()
$componentIds = [Collections.Generic.List[string]]::new()
$lines.Add('<?xml version="1.0" encoding="utf-8"?>')
$lines.Add('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
$lines.Add('  <Fragment>')
$lines.Add('    <DirectoryRef Id="INSTALLFOLDER">')
Add-DirectoryXml `
    -DirectoryPath $publishRoot `
    -RelativePath '' `
    -Lines $lines `
    -ComponentIds $componentIds `
    -Indent 6
$lines.Add('    </DirectoryRef>')
$lines.Add('  </Fragment>')
$lines.Add('  <Fragment>')
$lines.Add('    <ComponentGroup Id="PublishedFiles">')
foreach ($componentId in $componentIds) {
    $lines.Add("      <ComponentRef Id=`"$componentId`" />")
}
$lines.Add('    </ComponentGroup>')
$lines.Add('  </Fragment>')
$lines.Add('</Wix>')

$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
[IO.File]::WriteAllLines(
    $resolvedOutput,
    $lines,
    [Text.UTF8Encoding]::new($false))

Write-Output "WIX_FRAGMENT_OK|$($componentIds.Count)|$resolvedOutput"
