#requires -Version 7.0
param(
    [ValidateRange(1, 65535)]
    [int]$Port = 1313
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\scriptHelper.ps1"
Set-Location -LiteralPath $repoRoot

$siteRoot = "$repoRoot\musicApp.info"
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
    Write-Host "=== running $projectName.info... === `nq = quit `nr , up arrow = refresh"

    $restartRequested = $false
    $lineBuffer = ''
    while (-not $server.HasExited) {
        Start-Sleep -Milliseconds 50
        try { if (-not [Console]::KeyAvailable) { continue } } catch { continue }

        $key = [Console]::ReadKey($true)
        if ($key.Key -eq [ConsoleKey]::Q) {
            Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
            $keepRunning = $false
            break
        }
        if ($key.Key -eq [ConsoleKey]::R -or $key.Key -eq [ConsoleKey]::UpArrow) {
            Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
            $restartRequested = $true
            break
        }
        if ($key.Key -eq [ConsoleKey]::Enter) {
            Write-Host ''
            $userInput = $lineBuffer.Trim()
            $lineBuffer = ''
            if ($userInput -in @('q', 'quit', 'exit')) {
                Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
                $keepRunning = $false
                break
            }
            if ($userInput -in @('r', 'restart')) {
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
        Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
        $server.WaitForExit()
    }
    if ($restartRequested) { continue }
    if (-not $keepRunning) { break }
}
closeOut 0
