#Requires -Version 7.0
[CmdletBinding()]
param([string]$ArtifactPath)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'task-launcher-common.ps1')
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if(-not $ArtifactPath){$ArtifactPath=Join-Path $repoRoot 'output/mcp-secure-tunnel-20261023/task-launcher'}
$ArtifactPath=[IO.Path]::GetFullPath($ArtifactPath)
$manifest=Get-Content -LiteralPath (Join-Path $ArtifactPath 'build-manifest.json') -Raw|ConvertFrom-Json -AsHashtable
$stream=[IO.File]::OpenRead((Join-Path $ArtifactPath 'Meridian.TaskLauncher.exe'))
try{$reader=[IO.BinaryReader]::new($stream);$stream.Position=0x3c;$peOffset=$reader.ReadInt32();$stream.Position=$peOffset+24+68;if($reader.ReadUInt16() -ne 2){throw 'Published launcher is not Windows GUI subsystem.'}}finally{$stream.Dispose()}
$project=Join-Path $PSScriptRoot '../launcher/tests/Meridian.TaskLauncher.Tests.csproj'
& dotnet run --project $project -c Release "-p:BackendPowerShell=$($manifest.backendPowerShell)" "-p:SecurePowerShell=$($manifest.securePowerShell)" "-p:PowerShellSha256=$($manifest.powerShellSha256)" -- $ArtifactPath
if($LASTEXITCODE -ne 0){throw 'No-console launcher runtime tests failed.'}
$passed=[Collections.Generic.List[string]]::new()
function Check([string]$Name,[bool]$Condition){if(-not $Condition){throw "Launcher ownership test failed: $Name"};$passed.Add($Name)}
function Rejected([string]$Name,[scriptblock]$Operation){$failed=$false;try{&$Operation|Out-Null}catch{$failed=$true};Check $Name $failed}
$id=Get-BackendTaskIdentity
$task=[pscustomobject]@{Description=$id.description;Actions=@([pscustomobject]@{Execute=$id.launcherPath;Arguments=$id.arguments;WorkingDirectory=$id.workingDirectory});Principal=[pscustomobject]@{UserId=$id.userSid;LogonType='Interactive';RunLevel='Limited'}}
Assert-OwnedBackendTask $task|Out-Null;Check 'Owned GUI backend task accepted' $true
$task.Actions[0].Arguments='backend -Command calc';Rejected 'Extra backend arguments rejected' {Assert-OwnedBackendTask $task}
$task.Actions[0].Arguments=$id.arguments;$task.Actions[0].Execute='C:\Untrusted\Meridian.TaskLauncher.exe';Rejected 'Different launcher location rejected' {Assert-OwnedBackendTask $task}
$task.Actions[0].Execute=$id.powershellPath;$task.Actions[0].Arguments='-NoProfile -WindowStyle Hidden -File "'+$id.scriptPath+'"';$task.Description='Atlas Meridian local backend lifecycle v1; current-user installed application only'
Rejected 'Legacy console task rejected for routine ownership' {Assert-OwnedBackendTask $task}
Assert-OwnedBackendTask $task -AllowLegacyUpgrade|Out-Null;Check 'Exact legacy backend task accepted only for guarded upgrade' $true
$task.Actions[0].Execute='C:\Untrusted\pwsh.exe';Rejected 'Legacy basename alone cannot authorize upgrade' {Assert-OwnedBackendTask $task -AllowLegacyUpgrade}
[pscustomobject]@{passed=$passed.Count;checks=$passed;liveTasksTouched=$false}|ConvertTo-Json -Depth 4
