. "$PSScriptRoot\scriptHelper.ps1"
Write-Host "Restoring updater projects..." -ForegroundColor Yellow
dotnet restore "$updater\musicApp.Updater.csproj" --tl:off
dotnet restore "$updaterSwap\musicApp.Updater.Swap.csproj" --tl:off
Write-Host ""
foreach ($platform in 'x64', 'x86', 'arm64', 'portable') {
    Write-Host "Cleaning & building $platform updater then copying files..." -ForegroundColor Yellow
    Set-VersionBuildPlatform $platform
    dotnet clean "$updater\musicApp.Updater.csproj" -c Release -p:Platform="$platform" --tl:off
    dotnet build "$updater\musicApp.Updater.csproj" -c Release -p:Platform="$platform" --no-restore --tl:off
    dotnet clean "$updaterSwap\musicApp.Updater.Swap.csproj" -c Release -p:Platform="$platform" --tl:off
    dotnet build "$updaterSwap\musicApp.Updater.Swap.csproj" -c Release -p:Platform="$platform" --no-restore --tl:off
    $updBin = "$updater\bin\$platform\Release\net10.0-windows"
    $updSwapBin = "$updaterSwap\bin\$platform\Release\net10.0-windows"
    $releaseBin = "$appRoot\bin\$platform\Release\net10.0-windows"
    $updBin, $updSwapBin | ForEach-Object { Copy-Item "$_\*.json", "$_\*.dll", "$_\*.exe" $releaseBin }
    Write-Host ""
  }
