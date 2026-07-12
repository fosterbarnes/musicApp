$Host.UI.RawUI.WindowTitle = "Draft musicApp Release"
. "$PSScriptRoot\scriptHelper.ps1"; Set-Location $root
$portableRelease = "$appRoot\bin\portable\Release\net10.0-windows"
$portableZip = Join-Path $env:TEMP "musicApp_${versionContents}"
if (Test-Path $portableZip) { Remove-Item $portableZip -Recurse -Force }
Copy-Item -Path $portableRelease -Destination $portableZip -Recurse
Write-Host "Portable Release output (copied to temp for zip): $portableZip"
Write-Host "Version: $versionContents"
if (Test-Path -LiteralPath $buildNotes) {
    Write-Host "Release notes source: $buildNotes" -ForegroundColor Cyan
    $releaseNotes = ($buildNotesContents -replace "`t", "    ").Trim()
    if ([string]::IsNullOrWhiteSpace($releaseNotes)) {
        Write-Host "Error: buildNotes.txt is empty." -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "`nEnter release notes:" -ForegroundColor Yellow
    Write-Host "Tabs will be converted to spaces for GitHub formatting." -ForegroundColor Cyan
    $releaseNotesLines = @()
    $consecutiveEmptyLines = 0
    $hasReleaseNotes = $false

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
            $hasReleaseNotes = $true
        }
    }

    if (-not $hasReleaseNotes) {
        Write-Host "Error: No release notes entered." -ForegroundColor Red
        exit 1
    }

    $releaseNotes = $releaseNotesLines -join "`n"
}

$releaseTagSegment = Get-ReleaseTagSegment -Tag $versionTagContents
Write-Host "VersionTag raw: '$versionTagContents' => segment: '$releaseTagSegment'" -ForegroundColor Cyan

$v = $versionContents
$finalPortable = Join-Path $env:TEMP "musicApp-v${v}-${releaseTagSegment}-portable.zip"
$finalX64 = Join-Path $env:TEMP "musicApp-v${v}-${releaseTagSegment}-x64-installer.exe"
$finalX86 = Join-Path $env:TEMP "musicApp-v${v}-${releaseTagSegment}-x86-installer.exe"
$finalArm = Join-Path $env:TEMP "musicApp-v${v}-${releaseTagSegment}-arm64-installer.exe"
$tagName = "v$v"
$releaseName = "musicApp v$v $versionTagContents"

foreach ($p in @($finalPortable, $finalX64, $finalX86, $finalArm)) {
    if (Test-Path $p) { Remove-Item $p -Force -ErrorAction SilentlyContinue }
}

& 7z a -tzip -mx=5 "$finalPortable" "$portableZip\*"
Copy-Item -Path "$root\.installer\Output\musicApp-x64-installer.exe" -Destination $finalX64 -Force
Copy-Item -Path "$root\.installer\Output\musicApp-x86-installer.exe" -Destination $finalX86 -Force
Copy-Item -Path "$root\.installer\Output\musicApp-arm64-installer.exe" -Destination $finalArm -Force

if (git tag -l $tagName) {
    Write-Host "Local tag $tagName exists. Deleting..."
    git tag -d $tagName
}

$remoteTags = git ls-remote --tags origin | ForEach-Object { ($_ -split "`t")[1] }
if ($remoteTags -contains "refs/tags/$tagName") {
    Write-Host "Remote tag $tagName exists. Deleting..."
    git push origin --delete $tagName
}

$downloadsTable = @"
### Downloads

<table border="0">
<tbody>
<tr>
<td valign="top"><a href="https://github.com/fosterbarnes/musicApp/releases/download/$tagName/musicApp-${tagName}-${releaseTagSegment}-x64-installer.exe"><img src="https://raw.githubusercontent.com/fosterbarnes/musicApp/refs/heads/main/.resources/svg/download_x64.svg" width="180" height="auto" alt="x64 installer"/></a></td>
<td valign="top"><a href="https://github.com/fosterbarnes/musicApp/releases/download/$tagName/musicApp-${tagName}-${releaseTagSegment}-x86-installer.exe"><img src="https://raw.githubusercontent.com/fosterbarnes/musicApp/refs/heads/main/.resources/svg/download_x86.svg" width="180" height="auto" alt="x86 installer"/></a></td>
<td valign="top"><a href="https://github.com/fosterbarnes/musicApp/releases/download/$tagName/musicApp-${tagName}-${releaseTagSegment}-arm64-installer.exe"><img src="https://raw.githubusercontent.com/fosterbarnes/musicApp/refs/heads/main/.resources/svg/download_arm.svg" width="180" height="auto" alt="arm64 installer"/></a></td>
</tr>
<tr>
<td valign="top"><a href="https://github.com/fosterbarnes/musicApp/releases/download/$tagName/musicApp-${tagName}-${releaseTagSegment}-portable.zip"><img src="https://raw.githubusercontent.com/fosterbarnes/musicApp/refs/heads/main/.resources/svg/download_portable.svg" width="180" height="auto" alt="portable .zip"/></a></td>
</tr>
</tbody>
</table>

### Release notes

"@

$finalReleaseNotes = $downloadsTable + $releaseNotes

git tag $tagName && git push origin $tagName
& gh release create $tagName "$finalPortable" "$finalX64" "$finalX86" "$finalArm" --title "$releaseName" --notes "$finalReleaseNotes" --latest

Remove-Item -Path $finalPortable, $finalX64, $finalX86, $finalArm, $portableZip -Recurse -Force -ErrorAction SilentlyContinue
