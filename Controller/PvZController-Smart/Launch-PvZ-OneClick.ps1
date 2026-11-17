# One-click PvZ launcher — ensures bot controller is running, then starts PvZ
# Place a shortcut to this script on your Desktop. Double-clicking it will open PvZ and the bot together.

$ErrorActionPreference = 'Continue'
$gamePath = "G:\pvz\Game\Plants Vs Zombies GOTY\Plants vs. Zombies\PlantsVsZombies.exe"
$controllerDir = "G:\pvz\Controller\PvZController-Smart"
$watcher = Join-Path $controllerDir "Watch-PvZ.ps1"
$controllerExe = Join-Path $controllerDir "PvZController.exe"

# 1. Start watcher if not running (uses mutex, safe to call repeatedly)
try {
    $mutex = [Threading.Mutex]::OpenExisting('Local\PvZControllerWatcher')
    $mutex.Dispose()
    # watcher exists
} catch {
    Start-Process -FilePath "powershell.exe" -ArgumentList "-NoProfile","-WindowStyle","Hidden","-ExecutionPolicy","Bypass","-File", $watcher -WindowStyle Hidden
    Start-Sleep -Milliseconds 400
}

# 2. Start controller if not running
$ctrl = Get-Process -Name PvZController -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $ctrl) {
    Start-Process -FilePath $controllerExe -WorkingDirectory $controllerDir
    Start-Sleep -Milliseconds 600
}

# 3. Start game (also works via junction G:\Plants.Vs.Zombies.GOTY)
if (Test-Path -LiteralPath $gamePath) {
    Start-Process -FilePath $gamePath -WorkingDirectory (Split-Path -Parent $gamePath)
} else {
    [System.Windows.Forms.MessageBox]::Show("Game not found at $gamePath","PvZ Launcher",0,16) | Out-Null
}
