param(
    [ValidateRange(1, 65535)]
    [int]$Port = 1313
)

$root = Split-Path -Path $PSScriptRoot -Parent
$siteRoot = "$root\musicApp.info"
if (-not (Test-Path -LiteralPath $siteRoot -PathType Container)) {
    throw "Website folder not found: $siteRoot"
}

$siteUrl = "http://localhost:$Port"

function Start-WebsiteServer {
    $server = Start-Process `
        -FilePath 'py' `
        -ArgumentList @('-3', '-m', 'http.server', $Port, '--directory', $siteRoot) `
        -NoNewWindow `
        -PassThru

    Start-Sleep -Milliseconds 250
    Start-Process $siteUrl
    $server
}

$keepRunning = $true
while ($keepRunning) {
    $server = Start-WebsiteServer
    Write-Host "Serving musicApp.info at $siteUrl. Type 'q' to stop, 'r' or '￪' to refresh." -ForegroundColor Green

    $restartRequested = $false
    $lineBuffer = ''
    while (-not $server.HasExited) {
        Start-Sleep -Milliseconds 50
        try {
            if (-not [Console]::KeyAvailable) { continue }
        } catch {
            continue
        }

        $key = [Console]::ReadKey($true)
        if ($key.Key -eq [ConsoleKey]::UpArrow) {
            Write-Host 'Refreshing website...'
            Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
            $restartRequested = $true
            break
        }
        if ($key.KeyChar -eq 'q') {
            Write-Host 'Stopping website server and exiting script...'
            Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
            $keepRunning = $false
            break
        }
        if ($key.Key -eq [ConsoleKey]::Enter) {
            Write-Host ''
            $userInput = $lineBuffer.Trim()
            $lineBuffer = ''
            if ($userInput -in @('q', 'quit', 'exit')) {
                Write-Host 'Stopping website server and exiting script...'
                Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
                $keepRunning = $false
                break
            }
            if ($userInput -in @('r', 'restart')) {
                Write-Host 'Refreshing website...'
                Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
                $restartRequested = $true
                break
            }
            continue
        }
        if ($key.Key -eq [ConsoleKey]::Backspace) {
            if ($lineBuffer.Length -gt 0) {
                $lineBuffer = $lineBuffer.Substring(0, $lineBuffer.Length - 1)
                Write-Host "`b `b" -NoNewline
            }
            continue
        }
        if ($key.KeyChar -and -not [char]::IsControl($key.KeyChar)) {
            $lineBuffer += $key.KeyChar
            Write-Host -NoNewline $key.KeyChar
        }
    }

    if (-not $server.HasExited) {
        Stop-Process -Id $server.Id -Force
        $server.WaitForExit()
    }
    if ($restartRequested) {
        continue
    }
    if ($keepRunning) {
        Write-Host 'Website server stopped.'
    }
}
