# Dev runner: forwards args to `dotnet run ... -- <args>`. Use -h or --help for launch flags.
param(
    [Alias('h')]
    [switch]$Help,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AppLaunchArgs
)

function Show-MusicAppRunScriptHelp {
    Write-Host @"

  -h, -Help, --help, --h          Show help

Build (script only; not passed to the app; default portable / AnyCPU):
  --x86
  --x64
  --portable  (or --p)

  --settings / --s                Open Settings
  --info / --i                    Open Song info

Settings tabs:                    Song info tabs:
  --settings_general / --sg       --info_details / --id
  --settings_playback / --sp      --info_artwork / --ia
  --settings_library / --sl       --info_lyrics / --il
  --settings_shortcuts / --ss     --info_options / --io
  --settings_theme / --st         --info_sorting / --is
  --settings_about / --sa         --info_file / --if

While running, type q to quit, r to restart.
"@
}

if ($Help) {
    Show-MusicAppRunScriptHelp
    exit 0
}

$rawForward = [System.Collections.Generic.List[string]]::new()
if ($AppLaunchArgs) {
    foreach ($a in $AppLaunchArgs) { $rawForward.Add($a) }
}
if ($args.Count -gt 0) {
    foreach ($a in $args) { $rawForward.Add($a) }
}

$RunPlatform = 'AnyCPU'
$forward = [System.Collections.Generic.List[string]]::new()
foreach ($a in $rawForward) {
    if ([string]::IsNullOrWhiteSpace($a)) { continue }
    $t = $a.Trim()
    switch -Regex ($t) {
        '^(?i)(--help|--h|-h)$' {
            Show-MusicAppRunScriptHelp
            exit 0
        }
        '^(?i)--x86$' {
            $RunPlatform = 'x86'
            continue
        }
        '^(?i)--x64$' {
            $RunPlatform = 'x64'
            continue
        }
        '^(?i)(--portable|--p)$' {
            $RunPlatform = 'AnyCPU'
            continue
        }
        default {
            $forward.Add($a)
        }
    }
}

Set-Location (Split-Path -Parent $PSScriptRoot)
$versionBuildPath = Join-Path (Get-Location).Path 'VersionBuild'

$global:AppExited = $false
$global:KeepRunning = $true

function Start-MusicappRunner {
    switch ($RunPlatform) {
        'x86' { [System.IO.File]::WriteAllText($versionBuildPath, 'x86') }
        'x64' { [System.IO.File]::WriteAllText($versionBuildPath, 'x64') }
        'AnyCPU' { [System.IO.File]::WriteAllText($versionBuildPath, 'portable') }
    }

    $dotnetArgs = @(
        'run',
        '--project', 'musicApp.csproj',
        '-c', 'Release',
        "-p:Platform=$RunPlatform"
    )
    if ($forward.Count -gt 0) {
        $dotnetArgs += "--"
        $dotnetArgs += [string[]]$forward
    }

    $proc = Start-Process `
        -FilePath "dotnet" `
        -ArgumentList $dotnetArgs `
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
    $proc = Start-MusicappRunner
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
