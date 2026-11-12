#requires -Version 7.0
param([Alias('h')][switch]$Help, [string]$Architecture)
$ErrorActionPreference = 'Stop'
if ($Help) { Write-Host 'build.ps1 [-x86|-x64|-arm64]'; return }
. "$PSScriptRoot\scriptHelper.ps1"
Write-Host "=== building $projectName... ==="
Set-Location -LiteralPath $repoRoot
checkVerBuild $Architecture
$targetArchitecture = getArchitecture @($Architecture)
runNativeCommand dotnet @('restore', $csproj) 'dotnet restore'
foreach ($platform in (getMusicAppBuildPlatforms $targetArchitecture)) {
    setVerBuildPlatform $platform
    runNativeCommand dotnet @('clean', $csproj, '-c', 'Release', "-p:Platform=$platform") "dotnet clean $platform"
    runNativeCommand dotnet @('build', $csproj, '-c', 'Release', "-p:Platform=$platform", '--no-restore') "dotnet build $platform"
}
$updaterArgs = @()
if ($targetArchitecture) { $updaterArgs = @('-Architecture', $targetArchitecture) }
& "$PSScriptRoot\buildUpdater.ps1" @updaterArgs
if ($LASTEXITCODE) { throw 'buildUpdater.ps1 failed.' }
closeOut 3
