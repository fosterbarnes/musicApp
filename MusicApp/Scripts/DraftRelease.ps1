$Host.UI.RawUI.WindowTitle = "Draft musicApp Release"
. $PROFILE; Center-PSWindow

$basePath = (Get-Item $PSScriptRoot).Parent.FullName
$sourceBuild = Join-Path $basePath "bin\Release\net8.0-windows"
$versionFile = Join-Path $basePath "Version"
$version = (Get-Content $versionFile -First 1).Trim()
$versionTagFile = Join-Path $basePath "VersionTag"
$versionTag = (Get-Content $versionTagFile -First 1).Trim()
$buildFolder = Join-Path $env:TEMP "MusicApp_${version}"

if (Test-Path $buildFolder) { Remove-Item $buildFolder -Recurse -Force }
Copy-Item -Path $sourceBuild -Destination $buildFolder -Recurse
Write-Host "Using build folder (copied to temp): $buildFolder"
Write-Host "Version: $version"
Write-Host "`nEnter release notes:" -ForegroundColor Yellow
Write-Host "Tabs will be converted to spaces for GitHub formatting." -ForegroundColor Cyan

$releaseNotesLines = @()
$consecutiveEmptyLines = 0
$hasEnteredContent = $false

while ($true) {
    $line = Read-Host ">"
    if ($line -eq "") {
        $consecutiveEmptyLines++
        if ($consecutiveEmptyLines -ge 2) { break }
        $releaseNotesLines += ""
    } else {
        $line = $line -replace "`t", "    "
        $releaseNotesLines += $line
        $consecutiveEmptyLines = 0
        $hasEnteredContent = $true
    }
}

if (-not $hasEnteredContent) {
    Write-Host "Error: No release notes entered." -ForegroundColor Red
    exit 1
}

$releaseNotes = $releaseNotesLines -join "`n"
$zipPath = "$env:TEMP\musicApp_v${version}_${versionTag}.zip"
$tagName = "v$version"
$releaseName = "musicApp v$version $versionTag"
& 7z a -tzip -mx=5 "$zipPath" "$buildFolder\*"
$repoRoot = (Get-Item $basePath).Parent.FullName
Set-Location $repoRoot

if (git tag -l $tagName) {
    Write-Host "Local tag $tagName exists. Deleting..."
    git tag -d $tagName
}

$remoteTags = git ls-remote --tags origin | ForEach-Object { ($_ -split "`t")[1] }
if ($remoteTags -contains "refs/tags/$tagName") {
    Write-Host "Remote tag $tagName exists. Deleting..."
    git push origin --delete $tagName
}

git tag $tagName
git push origin $tagName
& gh release create $tagName "$zipPath" --title "$releaseName" --notes "$releaseNotes" --prerelease

Remove-Item -Path $zipPath, $buildFolder -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Temporary files cleaned up."
