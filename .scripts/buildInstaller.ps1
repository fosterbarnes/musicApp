#requires -Version 7.0
param([Alias('h')][switch]$Help, [string]$Architecture)
$ErrorActionPreference = 'Stop'
if ($Help) { Write-Host 'buildInstaller.ps1 [-x86|-x64|-arm64]'; return }
. "$PSScriptRoot\scriptHelper.ps1"
Write-Host "--- building $projectName installer... ---"
Set-Location -LiteralPath $repoRoot
$targetArchitecture = getArchitecture @($Architecture)
$iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue)?.Source
if (-not $iscc) { $iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' }
if (-not (Test-Path -LiteralPath $iscc)) { throw "Inno Setup compiler not found: $iscc" }
ensureInstallerImages
if (-not $Architecture) { deleteDir $installerOutput }
New-Item -ItemType Directory -Path $installerOutput -Force | Out-Null
$isccDefines = @(
    "/DAppVersion=$versionContents"
    "/DAppVersionTag=$versionTagContents"
    "/DAppPublisher=$appPublisher"
    "/DAppURL=$appURL"
)
foreach ($platform in (getMusicAppBuildPlatforms $targetArchitecture)) {
    setVerBuildPlatform $platform
    $iss = "$installerFolder\musicApp.$platform.installer.iss"
    if (-not (Test-Path -LiteralPath $iss)) { throw "Missing installer script: $iss" }
    runNativeCommand $iscc (@($isccDefines) + $iss) "ISCC $platform"
}
closeOut 3
