Set-Location (Split-Path -Parent $PSScriptRoot)
Write-Host "Building x64 installer..." -ForegroundColor Yellow
& ISCC.exe ".installer\musicApp.x64.installer.iss"
Write-Host "`nBuilding x86 installer..." -ForegroundColor Yellow
& ISCC.exe ".installer\musicApp.x86.installer.iss"