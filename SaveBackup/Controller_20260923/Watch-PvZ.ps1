$ErrorActionPreference = 'Continue'
$created = $false
$mutex = [Threading.Mutex]::new($true, 'Local\PvZControllerWatcher', [ref]$created)
if (-not $created) { exit }
try {
    $controllerExe = Join-Path $PSScriptRoot 'PvZController.exe'
    while ($true) {
        try {
            $game = Get-Process -Name PlantsVsZombies -ErrorAction SilentlyContinue | Select-Object -First 1
            $controller = Get-Process -Name PvZController -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($game -and -not $controller -and (Test-Path -LiteralPath $controllerExe)) {
                Start-Process -FilePath $controllerExe -WorkingDirectory $PSScriptRoot -WindowStyle Normal
            }
        } catch { }
        Start-Sleep -Seconds 2
    }
} finally {
    $mutex.ReleaseMutex()
    $mutex.Dispose()
}
