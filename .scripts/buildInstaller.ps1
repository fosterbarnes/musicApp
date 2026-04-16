. "$PSScriptRoot\scriptHelper.ps1"; Set-Location $appRoot
Write-Host "Cleaning old installers..." -ForegroundColor Yellow
Remove-Item -Path "$appRoot\.installer\Output\*" -Recurse -Force
$DAppVersion = "/DAppVersion=$versionContents"
$DAppVersionTag = "/DAppVersionTag=$versionTagContents"

foreach ($platform in 'x64', 'x86', 'arm64', 'portable') {
    Write-Host "Building $platform installer..." -ForegroundColor Yellow
    Set-VersionBuildPlatform $platform
    Write-Host "Wrote VersionBuild -> $platform ($versionBuild)" -ForegroundColor DarkGray
    & ISCC.exe $DAppVersion $DAppVersionTag "$appRoot\.installer\musicApp.$platform.installer.iss"
    Write-Host ""
}
