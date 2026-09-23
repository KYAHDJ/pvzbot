param([string]$OriginalExe)
# This script is invoked via IFEO Debugger for PlantsVsZombies.exe
# It ensures the bot controller is running, then launches the real game.

$ErrorActionPreference = 'Continue'

# 1. Ensure watcher is running (starts hidden powershell with mutex)
try {
    $watcher = "G:\pvz\Controller\PvZController-Smart\Watch-PvZ.ps1"
    $mutex = [Threading.Mutex]::OpenExisting('Local\PvZControllerWatcher')
    $mutex.Dispose()
} catch {
    # No watcher, start it
    Start-Process -FilePath "powershell.exe" -ArgumentList "-NoProfile","-WindowStyle","Hidden","-ExecutionPolicy","Bypass","-File", "G:\pvz\Controller\PvZController-Smart\Watch-PvZ.ps1" -WindowStyle Hidden
}

# 2. Ensure controller is running if game is about to start (watcher will do it within 2s, but start now for instant)
try {
    $ctrl = Get-Process -Name PvZController -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $ctrl) {
        Start-Process -FilePath "G:\pvz\Controller\PvZController-Smart\PvZController.exe" -WorkingDirectory "G:\pvz\Controller\PvZController-Smart" -WindowStyle Normal
    }
} catch {}

# 3. Launch the real game exe that was requested
# $OriginalExe is passed by IFEO as first arg, or if launched via shortcut it may be empty and we use known path
if (-not $OriginalExe -or -not (Test-Path -LiteralPath $OriginalExe)) {
    $OriginalExe = "G:\pvz\Game\Plants Vs Zombies GOTY\Plants vs. Zombies\PlantsVsZombies.exe"
}
# If OriginalExe is the powershell script itself (when IFEO passes), the real game is the second arg
# Check args
if ($args.Count -gt 0 -and (Test-Path -LiteralPath $args[0])) {
    $OriginalExe = $args[0]
}

try {
    Start-Process -FilePath $OriginalExe -WorkingDirectory (Split-Path -Parent $OriginalExe)
} catch {
    # Fallback to known path
    Start-Process -FilePath "G:\pvz\Game\Plants Vs Zombies GOTY\Plants vs. Zombies\PlantsVsZombies.exe" -WorkingDirectory "G:\pvz\Game\Plants Vs Zombies GOTY\Plants vs. Zombies"
}
