# Dev runner: forwards args to `dotnet run ... -- <args>`. Use -h or --help for launch flags.
param(
    [Alias('h')]
    [switch]$Help,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AppLaunchArgs
)

function Show-RunHelp {
    Write-Host @"
Launch flags:
  --help,--h                            Show help (this menu)

Platform:
  --x86
  --x64
  --arm64
  --portable, --p

Launch menu on startup:
  --settings, --s                       Open Settings menu
  --settings_general , --sg
  --settings_playback , --sp
  --settings_library , --sl
  --settings_shortcuts , --ss
  --settings_theme , --st    
  --settings_about , --sa

  --info, --i                           Open Song info menu
  --info_details , --id
  --info_artwork , --ia
  --info_lyrics , --il
  --info_options , --io
  --info_sorting , --is
  --info_file , --if

Run Options:
  q, quit, exit                         Stop the app and exit the script
  r, restart, ￪                         Restart the app
"@
}

if ($Help) {
    Show-RunHelp
    exit 0
}

. "$PSScriptRoot\scriptHelper.ps1"; Set-Location $appRoot

$RunPlatform = 'portable'
$forward = @()
foreach ($a in @($AppLaunchArgs) + @($args)) {
    if ($null -eq $a -or [string]::IsNullOrWhiteSpace($a)) { continue }
    $t = $a.Trim()
    switch -Regex ($t) {
        '^(?i)(--help|--h|-h)$' {
            Show-RunHelp
            exit 0
        }
        '^(?i)(--x86|--86|-x86|-86)$' { $RunPlatform = 'x86'; continue }
        '^(?i)(--x64|--64|-x64|-64)$' { $RunPlatform = 'x64'; continue }
        '^(?i)(--arm64|--arm|-arm64|-arm)$' { $RunPlatform = 'arm64'; continue }
        '^(?i)(--portable|--p|-portable|-p)$' { $RunPlatform = 'portable'; continue }
        default { $forward += $a }
    }
}

$keepRunning = $true
while ($keepRunning) {
    Set-VersionBuildPlatform $RunPlatform

    $dotnetArgs = @(
        'run',
        '--project', $musicAppCsproj,
        '-c', 'Release',
        "-p:Platform=$RunPlatform"
    )
    if ($forward.Count -gt 0) {
        $dotnetArgs += '--'
        $dotnetArgs += [string[]]$forward
    }

    $proc = Start-Process `
        -FilePath 'dotnet' `
        -ArgumentList $dotnetArgs `
        -WorkingDirectory $appRoot `
        -NoNewWindow `
        -PassThru

    Write-Host "musicApp is running. Type 'q' to stop, 'r' or '￪' to restart."

    $restartRequested = $false
    $lineBuffer = ''

    while (-not $proc.HasExited) {
        Start-Sleep -Milliseconds 50
        try {
            if (-not [Console]::KeyAvailable) { continue }
        } catch {
            continue
        }

        $key = [Console]::ReadKey($true)
        if ($key.Key -eq [ConsoleKey]::UpArrow) {
            Write-Host 'Restarting musicApp...'
            Stop-Process -Name 'musicApp' -Force -ErrorAction SilentlyContinue
            $restartRequested = $true
            break
        }
        if ($key.Key -eq [ConsoleKey]::Enter) {
            Write-Host ''
            $userInput = $lineBuffer.Trim()
            $lineBuffer = ''
            if ($userInput -in @('q', 'quit', 'exit')) {
                Write-Host 'Stopping musicApp and exiting script...'
                Stop-Process -Name 'musicApp' -Force -ErrorAction SilentlyContinue
                $keepRunning = $false
                break
            }
            if ($userInput -in @('r', 'restart')) {
                Write-Host 'Restarting musicApp...'
                Stop-Process -Name 'musicApp' -Force -ErrorAction SilentlyContinue
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

    if ($proc.HasExited) {
        Write-Host 'musicApp stopped.'
        try {
            $code = $proc.ExitCode
            if ($null -ne $code -and $code -ne 0) {
                Write-Host "dotnet exit code: $code" -ForegroundColor Red
            }
        } catch { }
    }

    if (-not $keepRunning -or -not $restartRequested) { break }
}
