#requires -Version 7.0
param([Alias('h')][switch]$Help, [string]$Architecture)
$ErrorActionPreference = 'Stop'
if ($Help) { Write-Host 'buildUpdater.ps1 [-x86|-x64|-arm64]'; return }
. "$PSScriptRoot\scriptHelper.ps1"
Write-Host "--- building $projectName updater... ---"
Set-Location -LiteralPath $repoRoot
$targetArchitecture = getArchitecture @($Architecture)
runNativeCommand dotnet @('restore', $updaterCsproj) 'dotnet restore updater'
runNativeCommand dotnet @('restore', $updaterSwapCsproj) 'dotnet restore updater swap'
foreach ($platform in (getMusicAppBuildPlatforms $targetArchitecture)) {
    setVerBuildPlatform $platform
    runNativeCommand dotnet @('clean', $updaterCsproj, '-c', 'Release', "-p:Platform=$platform") "dotnet clean updater $platform"
    runNativeCommand dotnet @('build', $updaterCsproj, '-c', 'Release', "-p:Platform=$platform", '--no-restore') "dotnet build updater $platform"
    runNativeCommand dotnet @('clean', $updaterSwapCsproj, '-c', 'Release', "-p:Platform=$platform") "dotnet clean updater swap $platform"
    runNativeCommand dotnet @('build', $updaterSwapCsproj, '-c', 'Release', "-p:Platform=$platform", '--no-restore') "dotnet build updater swap $platform"
    $updBin = "$appRoot\.updater\bin\$platform\Release\net10.0-windows"
    $updSwapBin = "$appRoot\.updater.swap\bin\$platform\Release\net10.0-windows"
    $releaseBin = "$appRoot\bin\$platform\Release\net10.0-windows"
    foreach ($bin in @($updBin, $updSwapBin)) {
        Copy-Item "$bin\*.json", "$bin\*.dll", "$bin\*.exe" $releaseBin -Force
    }
}
closeOut 3
