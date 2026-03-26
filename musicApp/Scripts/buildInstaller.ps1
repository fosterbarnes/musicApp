$versionBuildPath = Join-Path (Split-Path $PSScriptRoot -Parent) "VersionBuild"
Set-Location (Split-Path -Parent $PSScriptRoot)

Write-Host "Building x64 installer..." -ForegroundColor Yellow
[System.IO.File]::WriteAllText($versionBuildPath, "x64")
Write-Host "Wrote VersionBuild -> x64 ($versionBuildPath)" -ForegroundColor DarkGray
& ISCC.exe ".installer\musicApp.x64.installer.iss"

Write-Host "`nBuilding x86 installer..." -ForegroundColor Yellow
[System.IO.File]::WriteAllText($versionBuildPath, "x86")
Write-Host "Wrote VersionBuild -> x86 ($versionBuildPath)" -ForegroundColor DarkGray
& ISCC.exe ".installer\musicApp.x86.installer.iss"