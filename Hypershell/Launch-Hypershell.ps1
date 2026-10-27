[CmdletBinding()]
param(
    [switch]$RestartRunning,
    [switch]$NonInteractive
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSCommandPath
$projectPath = Join-Path $projectRoot 'src\Hypershell\Hypershell.csproj'
$executablePath = Join-Path $projectRoot 'src\Hypershell\bin\Debug\net10.0-windows\Hypershell.exe'

Add-Type -AssemblyName PresentationFramework

function Show-LauncherMessage {
    param(
        [string]$Message,
        [System.Windows.MessageBoxImage]$Image = [System.Windows.MessageBoxImage]::Information
    )

    if ($NonInteractive) {
        Write-Output "LAUNCHER ERROR: $Message"
    }
    else {
        [void][System.Windows.MessageBox]::Show(
            $Message,
            'Hypershell launcher',
            [System.Windows.MessageBoxButton]::OK,
            $Image)
    }
}

if (-not (Test-Path -LiteralPath $projectPath)) {
    Show-LauncherMessage "The Hypershell project could not be found at:`n$projectPath" ([System.Windows.MessageBoxImage]::Error)
    exit 1
}

$runningApps = @(Get-Process -Name Hypershell -ErrorAction SilentlyContinue)
if ($runningApps.Count -gt 0) {
    $shouldRestart = $RestartRunning
    if (-not $shouldRestart -and -not $NonInteractive) {
        $response = [System.Windows.MessageBox]::Show(
            'Hypershell is already running. Close that session, build the latest source, and launch the new version?',
            'Hypershell launcher',
            [System.Windows.MessageBoxButton]::YesNo,
            [System.Windows.MessageBoxImage]::Question)
        $shouldRestart = $response -eq [System.Windows.MessageBoxResult]::Yes
    }

    if (-not $shouldRestart) {
        if ($NonInteractive) {
            Write-Output 'LAUNCHER CANCELLED: Hypershell is already running.'
        }
        exit 0
    }

    $runningIds = @($runningApps.Id)
    foreach ($runningApp in $runningApps) {
        [void]$runningApp.CloseMainWindow()
    }

    # Terminal runspaces release asynchronously. Give the application enough
    # time to dispose them before rebuilding the executable it has locked.
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 200
        $remainingApps = @($runningIds | ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
    } while ($remainingApps.Count -gt 0 -and [DateTime]::UtcNow -lt $deadline)

    if ($remainingApps.Count -gt 0) {
        Show-LauncherMessage 'Hypershell did not close cleanly. Close it yourself, then run the launcher again.' ([System.Windows.MessageBoxImage]::Warning)
        exit 1
    }
}

try {
    # Recompile application resources on every launch so bundled font, image,
    # and XAML changes can never be hidden by an incremental build cache.
    $buildOutput = (& dotnet build $projectPath --nologo --no-incremental 2>&1 | Out-String)
}
catch {
    Show-LauncherMessage "Unable to start the .NET build.`n`n$($_.Exception.Message)" ([System.Windows.MessageBoxImage]::Error)
    exit 1
}

if ($LASTEXITCODE -ne 0) {
    $summary = @($buildOutput -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -Last 18) -join [Environment]::NewLine
    Show-LauncherMessage "Hypershell could not build. The last build details were:`n`n$summary" ([System.Windows.MessageBoxImage]::Error)
    exit 1
}

if (-not (Test-Path -LiteralPath $executablePath)) {
    Show-LauncherMessage "The build succeeded, but the application executable was not found at:`n$executablePath" ([System.Windows.MessageBoxImage]::Error)
    exit 1
}

try {
    $launchedApp = Start-Process -FilePath $executablePath -WorkingDirectory (Split-Path -Parent $executablePath) -PassThru
    [void]$launchedApp.WaitForInputIdle(10000)
    Start-Sleep -Seconds 2
    if ($launchedApp.HasExited) {
        throw 'Hypershell exited before its window could finish initializing.'
    }
}
catch {
    Show-LauncherMessage "The latest Hypershell build could not be launched.`n`n$($_.Exception.Message)" ([System.Windows.MessageBoxImage]::Error)
    exit 1
}
