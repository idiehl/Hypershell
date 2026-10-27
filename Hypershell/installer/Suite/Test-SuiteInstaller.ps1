[CmdletBinding()]
param([switch] $Build)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixture = Join-Path $repository ('output/suite-installer-test/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null

function Require([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}
function Write-Fixture([string] $Relative, [string] $Text = 'fixture - not a distributable application') {
    $path = Join-Path $fixture $Relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    [IO.File]::WriteAllText($path, $Text)
}
function Invoke-Checked([string[]] $Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Suite fixture build failed: $($Arguments -join ' ')" }
}
function Assert-Rejected([scriptblock] $Action, [string] $Expected) {
    try { & $Action | Out-Null }
    catch {
        Require ($_.Exception.Message.Contains($Expected)) "Unexpected rejection: $($_.Exception.Message)"
        return
    }
    throw "Expected rejection: $Expected"
}
function Read-MsiValues([string] $Path, [string] $Query) {
    $installer = $database = $view = $record = $null
    $values = [Collections.Generic.List[string]]::new()
    try {
        $installer = New-Object -ComObject WindowsInstaller.Installer
        $database = $installer.OpenDatabase($Path, 0)
        $view = $database.OpenView($Query)
        [void] $view.Execute()
        while ($null -ne ($record = $view.Fetch())) {
            $values.Add([string] $record.StringData(1))
            [void] [Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
            $record = $null
        }
        return $values.ToArray()
    }
    finally {
        foreach ($instance in @($record, $view, $database, $installer)) {
            if ($null -ne $instance -and [Runtime.InteropServices.Marshal]::IsComObject($instance)) {
                [void] [Runtime.InteropServices.Marshal]::FinalReleaseComObject($instance)
            }
        }
    }
}

foreach ($path in @('authoring/studio/Hypershell.exe', 'authoring/studio/Hypershell.dll',
    'authoring/Hyperlib/hyperlib.app.json', 'authoring/Hyperlib/app/HyperLib.Host.exe',
    'authoring/Hyperlib/Starters/standard/src/Host/Host.csproj', 'authoring/Hyperlib/Starters/standard/src/Pages/Pages.csproj',
    'authoring/Hyperlib/BaseTemplates/example/default.json',
    'meridian/Meridian.Host.exe', 'meridian/Meridian.Host.dll', 'meridian/wwwroot/index.html')) {
    Write-Fixture $path
}
$authoring = Join-Path $fixture 'authoring'
$meridian = Join-Path $fixture 'meridian'
$authoringFragment = Join-Path $fixture 'AuthoringFiles.wxs'
$meridianFragment = Join-Path $fixture 'MeridianFiles.wxs'
& "$PSScriptRoot/Generate-ComponentFiles.ps1" -ComponentName Authoring -PublishDirectory $authoring -OutputPath $authoringFragment
$firstHash = (Get-FileHash -LiteralPath $authoringFragment).Hash
& "$PSScriptRoot/Generate-ComponentFiles.ps1" -ComponentName Authoring -PublishDirectory $authoring -OutputPath $authoringFragment
Require ($firstHash -eq (Get-FileHash -LiteralPath $authoringFragment).Hash) 'Component harvesting must be deterministic.'
& "$PSScriptRoot/Generate-ComponentFiles.ps1" -ComponentName Meridian -PublishDirectory $meridian -OutputPath $meridianFragment

Write-Fixture 'authoring/.env'
Assert-Rejected { & "$PSScriptRoot/Generate-ComponentFiles.ps1" -ComponentName Authoring -PublishDirectory $authoring -OutputPath $authoringFragment } 'private data'
Remove-Item -LiteralPath (Join-Path $authoring '.env')
Write-Fixture 'authoring/.hyperlib/runtime/personal-state.json'
Assert-Rejected { & "$PSScriptRoot/Generate-ComponentFiles.ps1" -ComponentName Authoring -PublishDirectory $authoring -OutputPath $authoringFragment } 'private data'
# Remove only the three explicitly authored fixture entries, without recursion.
Remove-Item -LiteralPath (Join-Path $authoring '.hyperlib/runtime/personal-state.json')
Remove-Item -LiteralPath (Join-Path $authoring '.hyperlib/runtime')
Remove-Item -LiteralPath (Join-Path $authoring '.hyperlib')
Assert-Rejected { & "$PSScriptRoot/Generate-ComponentFiles.ps1" -ComponentName Authoring -PublishDirectory $authoring -OutputPath (Join-Path $authoring 'Files.wxs') } 'outside'
Remove-Item -LiteralPath (Join-Path $meridian 'Meridian.Host.dll')
Assert-Rejected { & "$PSScriptRoot/Generate-ComponentFiles.ps1" -ComponentName Meridian -PublishDirectory $meridian -OutputPath $meridianFragment } 'incomplete'
Write-Fixture 'meridian/Meridian.Host.dll'
$link = Join-Path $authoring 'linked-content'
[IO.Directory]::CreateSymbolicLink($link, $meridian) | Out-Null
try {
    Assert-Rejected { & "$PSScriptRoot/Generate-ComponentFiles.ps1" -ComponentName Authoring -PublishDirectory $authoring -OutputPath $authoringFragment } 'Symbolic links'
}
finally { [IO.Directory]::Delete($link) }
$linkedRoot = Join-Path $fixture 'linked-root'
[IO.Directory]::CreateSymbolicLink($linkedRoot, $fixture) | Out-Null
try {
    Assert-Rejected { & "$PSScriptRoot/Generate-ComponentFiles.ps1" -ComponentName Authoring -PublishDirectory (Join-Path $linkedRoot 'authoring') -OutputPath $authoringFragment } 'symbolic link'
}
finally { [IO.Directory]::Delete($linkedRoot) }

$bundle = [xml](Get-Content -LiteralPath "$PSScriptRoot/Bundle.wxs" -Raw)
$namespace = [Xml.XmlNamespaceManager]::new($bundle.NameTable)
$namespace.AddNamespace('w', 'http://wixtoolset.org/schemas/v4/wxs')
$packages = @($bundle.SelectNodes('//w:Chain/*', $namespace))
Require ($packages.Count -eq 5) 'Complete must contain both prerequisites and all three product packages.'
Require ($packages[0].Id -eq 'WebView2Runtime' -and $packages[0].Permanent -eq 'yes') 'WebView2 must precede product packages and remain shared.'
Require ($packages[1].Id -eq 'AuthoringSdk' -and $packages[1].InstallCondition -eq 'InstallAuthoring = 1') 'Studio SDK prerequisite must follow authoring selection.'
Require ($packages[2].Id -eq 'HypershellCore') 'Core must precede optional components.'
Require ($packages[3].InstallCondition -eq 'InstallAuthoring = 1' -and $packages[4].InstallCondition -eq 'InstallMeridian = 1') 'Both optional component selections must control their MSI.'

if ($Build) {
    # Compile inert payloads. These outputs must never be installed or published.
    $version = '0.0.1'
    foreach ($name in @('Authoring', 'Meridian')) {
        $fragment = Join-Path $fixture ($name + 'Files.wxs')
        Invoke-Checked @('build', "$PSScriptRoot/Hypershell.Component.wixproj", '-c', 'Release',
            "-p:ProductVersion=$version", "-p:ComponentName=$name", "-p:GeneratedFilesPath=$fragment",
            "-p:BaseIntermediateOutputPath=$fixture/obj-$name/", '-o', "$fixture/msi-$name", '--nologo')
        $componentMsi = "$fixture/msi-$name/Hypershell-$name-$version-win-x64.msi"
        $scope = @(Read-MsiValues $componentMsi 'SELECT `Value` FROM `Property` WHERE `Property`=''ALLUSERS''')
        Require ($scope.Count -eq 1 -and $scope[0] -eq '1') 'Components must install per machine.'
        $componentVersion = @(Read-MsiValues $componentMsi 'SELECT `Value` FROM `Property` WHERE `Property`=''ProductVersion''')
        Require ($componentVersion.Count -eq 1 -and $componentVersion[0] -eq $version) 'Components must share the suite version.'
        $shortcut = @(Read-MsiValues $componentMsi 'SELECT `Target` FROM `Shortcut`')
        $expectedTarget = if ($name -eq 'Authoring') { '[COMPONENTFOLDER]studio\Hypershell.exe' } else { '[HypershellFolder]Hypershell.exe' }
        Require ($shortcut.Count -eq 1 -and $shortcut[0] -eq $expectedTarget) 'Component shortcuts must use install-relative executables.'
    }
    # A third distinct component MSI is unnecessary: the real Core MSI authoring
    # can be compiled around an inert executable file without running it.
    Write-Fixture 'core/Hypershell.exe'
    & "$repository/installer/Generate-WixFiles.ps1" -PublishDirectory "$fixture/core" -OutputPath "$fixture/CoreFiles.wxs"
    Invoke-Checked @('build', "$repository/installer/Hypershell.Installer.wixproj", '-c', 'Release',
        "-p:ProductVersion=$version", "-p:GeneratedFilesPath=$fixture/CoreFiles.wxs",
        "-p:BaseIntermediateOutputPath=$fixture/obj-Core/", '-o', "$fixture/msi-Core", '--nologo')
    $core = @(Get-ChildItem -LiteralPath "$fixture/msi-Core" -Filter '*.msi')
    Require ($core.Count -eq 1) 'Core fixture must produce one MSI.'
    $conditions = @(Read-MsiValues $core[0].FullName 'SELECT `Condition` FROM `LaunchCondition`')
    Require (@($conditions | Where-Object { $_.Contains('WEBVIEW2_MACHINE_VERSION') -and $_.Contains('WEBVIEW2_USER_VERSION') }).Count -eq 1) 'Core must detect WebView2 before first installation.'
    $nativeFixture = Join-Path ([Environment]::SystemDirectory) 'whoami.exe'
    $sdkFixture = Join-Path ([Environment]::SystemDirectory) 'hostname.exe'
    Invoke-Checked @('build', "$PSScriptRoot/Hypershell.Complete.wixproj", '-c', 'Release',
        "-p:ProductVersion=$version", "-p:CoreInstallerPath=$($core[0].FullName)",
        "-p:AuthoringInstallerPath=$fixture/msi-Authoring/Hypershell-Authoring-$version-win-x64.msi",
        "-p:MeridianInstallerPath=$fixture/msi-Meridian/Hypershell-Meridian-$version-win-x64.msi",
        "-p:WebView2InstallerPath=$nativeFixture", "-p:DotNetSdkInstallerPath=$sdkFixture",
        "-p:BaseIntermediateOutputPath=$fixture/obj-Complete/", '-o', "$fixture/bundle", '--nologo')
    Require ([IO.File]::Exists("$fixture/bundle/Hypershell-Complete-$version-win-x64.exe")) 'Complete fixture bundle was not built.'
}
Write-Output "HYPERSHELL_SUITE_INSTALLER_OK|deterministic|private-state-rejected|incomplete-rejected|prerequisites|selection|built:$($Build.IsPresent)|$fixture"
