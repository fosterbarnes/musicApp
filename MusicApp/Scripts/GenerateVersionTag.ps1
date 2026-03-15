$exe = "C:\Users\Foster\Documents\GitHub\YapHeader\YapCli.exe"
$versionTagPath = Join-Path (Split-Path -Parent $PSScriptRoot) "VersionTag"
$output = & $exe
$output | Set-Content -Path $versionTagPath
Write-Host “$output”
