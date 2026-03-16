Set-Location (Split-Path -Parent $PSScriptRoot)

$global:AppExited = $false
$global:KeepRunning = $true

function Start-MusicApp {
    $proc = Start-Process `
        -FilePath "dotnet" `
        -ArgumentList "run --project musicApp.csproj -c Release" `
        -WorkingDirectory (Get-Location).Path `
        -WindowStyle Hidden `
        -PassThru

    $global:AppExited = $false

    $proc.EnableRaisingEvents = $true
    $null = Register-ObjectEvent -InputObject $proc -EventName Exited -SourceIdentifier "musicApp_Exited" -Action {
        $global:AppExited = $true
        Write-Host "musicApp stopped."
    }

    return $proc
}

while ($global:KeepRunning) {
    $proc = Start-MusicApp
    Write-Host "musicApp is running. Type 'q' to stop, 'r' to restart."

    $restartRequested = $false

    try {
        while (-not $global:AppExited) {
            Start-Sleep -Milliseconds 5
            if ($global:AppExited) { break }
            try {
                if (-not [Console]::KeyAvailable) { continue }
            } catch {
                continue
            }
            $userInput = Read-Host
            if ($userInput -in @('q', 'quit', 'exit')) {
                Write-Host "Stopping musicApp and exiting script..."
                Stop-Process -Name "musicApp" -Force -ErrorAction SilentlyContinue
                $global:KeepRunning = $false
                break
            }
            elseif ($userInput -in @('r', 'restart')) {
                Write-Host "Restarting musicApp..."
                Stop-Process -Name "musicApp" -Force -ErrorAction SilentlyContinue
                $restartRequested = $true
                break
            }
        }
    }
    finally {
        Unregister-Event -SourceIdentifier "musicApp_Exited" -ErrorAction SilentlyContinue
    }

    if (-not $restartRequested) {
        break
    }
}

Write-Host "exiting script..."