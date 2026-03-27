$musicAppRoot = Split-Path -Parent $PSScriptRoot
$versionBuildPath = Join-Path $musicAppRoot "VersionBuild"
$versionPath = Join-Path $musicAppRoot "Version"
$versionTagPath = Join-Path $musicAppRoot "VersionTag"
Set-Location $musicAppRoot

$appVersion = [System.IO.File]::ReadAllText($versionPath).Trim()
if ([string]::IsNullOrWhiteSpace($appVersion)) {
    throw "Version file is missing or empty: $versionPath"
}
$appVersionTag = [System.IO.File]::ReadAllText($versionTagPath).Trim()
if ([string]::IsNullOrWhiteSpace($appVersionTag)) {
    throw "VersionTag file is missing or empty: $versionTagPath"
}
$defineVersion = "/DMyAppVersion=$appVersion"
$defineVersionTag = "/DMyAppVersionTag=$appVersionTag"

Write-Host "Building x64 installer (AppVersion=$appVersion, Tag=$appVersionTag)..." -ForegroundColor Yellow
[System.IO.File]::WriteAllText($versionBuildPath, "x64")
Write-Host "Wrote VersionBuild -> x64 ($versionBuildPath)" -ForegroundColor DarkGray
& ISCC.exe $defineVersion $defineVersionTag ".installer\musicApp.x64.installer.iss"

Write-Host "`nBuilding x86 installer (AppVersion=$appVersion, Tag=$appVersionTag)..." -ForegroundColor Yellow
[System.IO.File]::WriteAllText($versionBuildPath, "x86")
Write-Host "Wrote VersionBuild -> x86 ($versionBuildPath)" -ForegroundColor DarkGray
& ISCC.exe $defineVersion $defineVersionTag ".installer\musicApp.x86.installer.iss"