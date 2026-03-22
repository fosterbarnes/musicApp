Write-Host "Building musicApp for x86..." -ForegroundColor Yellow
dotnet build musicApp.csproj -c Release -p:Platform=x86
Write-Host "`nBuilding musicApp for x64..." -ForegroundColor Yellow
dotnet build musicApp.csproj -c Release -p:Platform=x64
Write-Host "`nBuilding musicApp for AnyCPU..." -ForegroundColor Yellow
dotnet build musicApp.csproj -c Release -p:Platform=AnyCPU
& (Join-Path $PSScriptRoot "buildInstaller.ps1")