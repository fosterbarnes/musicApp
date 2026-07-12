. "$PSScriptRoot\scriptHelper.ps1"; Set-Location $appRoot
Write-Host "Cleaning old installers..." -ForegroundColor Yellow
Remove-Item -Path "$root\.installer\Output\*" -Recurse -Force
$DAppVersion = "/DAppVersion=$versionContents"
$DAppVersionTag = "/DAppVersionTag=$versionTagContents"

foreach ($platform in 'x64', 'x86', 'arm64', 'portable') {
    Write-Host "Building $platform installer..." -ForegroundColor Yellow
    Set-VersionBuildPlatform $platform
    Write-Host "Wrote Version build line -> $platform" -ForegroundColor DarkGray
    & ISCC.exe $DAppVersion $DAppVersionTag "$root\.installer\musicApp.$platform.installer.iss"
    Write-Host ""
}
