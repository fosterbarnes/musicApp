#requires -Version 7.0
# Packages the portable zip and installers prePush.ps1 built, tags v<version>, and publishes the GitHub
# release with the buildNotes.txt body (below line 1) as notes. -DryRun prints the commands instead.
param([Alias('n')][switch]$DryRun)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\scriptHelper.ps1"
Set-Location -LiteralPath $repoRoot
$portableRelease = "$appRoot\bin\portable\Release\net10.0-windows"
# An older build would ship under the new version's name, so the build must match the version file.
$builtVersionFile = "$portableRelease\Version"
$built = if (Test-Path -LiteralPath $builtVersionFile) { (readVerFile -LiteralPath $builtVersionFile)[0..1] -join ' ' } else { 'missing' }
if ($built -ne "$versionContents $versionTagContents") {
    throw "Portable build is '$built', not '$versionContents $versionTagContents': run prePush.ps1 first."
}
$portableZip = Join-Path $env:TEMP "musicApp_${versionContents}"
if (Test-Path $portableZip) { Remove-Item $portableZip -Recurse -Force }
Copy-Item -Path $portableRelease -Destination $portableZip -Recurse
Write-Host "Portable Release output (copied to temp for zip): $portableZip"
Write-Host "Version: $versionContents"
if (Test-Path -LiteralPath $buildNotes) {
    $lines = @([IO.File]::ReadAllLines($buildNotes))
    if ($lines.Count -ge 2 -and $lines[1].Trim()) {
        throw 'buildNotes.txt must have one blank line after the first line, then the release notes.'
    }
    $releaseNotes = if ($lines.Count -gt 2) { (($lines[2..($lines.Count - 1)] -join "`n") -replace "`t", '    ').Trim() } else { '' }
    if (-not $releaseNotes) { throw 'buildNotes.txt has no release notes below line 1.' }
} else {
    Write-Host ''
    Write-Host 'Enter release notes:'
    Write-Host 'Tabs will be converted to spaces for GitHub formatting.'
    $releaseNotesLines = @()
    $consecutiveEmptyLines = 0
    $hasReleaseNotes = $false

    while ($true) {
        $line = Read-Host '>'
        if ($line -eq '') {
            $consecutiveEmptyLines++
            if ($consecutiveEmptyLines -ge 2) { break }
            $releaseNotesLines += ''
        } else {
            $line = $line -replace "`t", '    '
            $releaseNotesLines += $line
            $consecutiveEmptyLines = 0
            $hasReleaseNotes = $true
        }
    }

    if (-not $hasReleaseNotes) { throw 'No release notes entered.' }

    $releaseNotes = $releaseNotesLines -join "`n"
}

$releaseTagSegment = getReleaseTagSegment -Tag $versionTagContents
Write-Host "VersionTag raw: '$versionTagContents' => segment: '$releaseTagSegment'"

$v = $versionContents
$finalPortable = Join-Path $env:TEMP "musicApp-v${v}-${releaseTagSegment}-portable.zip"
$finalX64 = Join-Path $env:TEMP "musicApp-v${v}-${releaseTagSegment}-x64-installer.exe"
$finalX86 = Join-Path $env:TEMP "musicApp-v${v}-${releaseTagSegment}-x86-installer.exe"
$finalArm = Join-Path $env:TEMP "musicApp-v${v}-${releaseTagSegment}-arm64-installer.exe"
$tagName = "v$v"
# Installed updaters read the tag from this title (GitHubTagsVersionService.TryParseVersionTagFromReleaseName)
# to build the asset names above. Keep the format.
$releaseName = "musicApp v$v $versionTagContents"

foreach ($p in @($finalPortable, $finalX64, $finalX86, $finalArm)) {
    if (Test-Path $p) { Remove-Item $p -Force -ErrorAction SilentlyContinue }
}

runNativeCommand -FilePath '7z' -ArgumentList @('a', '-tzip', '-mx=5', $finalPortable, "$portableZip\*") -Name '7z portable zip'
Copy-Item -Path "$installerOutput\musicApp-x64-installer.exe" -Destination $finalX64 -Force
Copy-Item -Path "$installerOutput\musicApp-x86-installer.exe" -Destination $finalX86 -Force
Copy-Item -Path "$installerOutput\musicApp-arm64-installer.exe" -Destination $finalArm -Force

if ($DryRun) {
    Write-Host "Dry run: git tag $tagName"
    Write-Host "Dry run: git push origin $tagName"
    Write-Host "Dry run: gh release create $tagName"
    Write-Host "Dry run: open $appURL/releases/tag/$tagName"
    Remove-Item -Path $finalPortable, $finalX64, $finalX86, $finalArm, $portableZip -Recurse -Force -ErrorAction SilentlyContinue
    return
}

if (git tag -l $tagName) {
    Write-Host "Local tag $tagName exists. Deleting..."
    git tag -d $tagName
}

$remoteTags = git ls-remote --tags origin | ForEach-Object { ($_ -split "`t")[1] }
if ($remoteTags -contains "refs/tags/$tagName") {
    Write-Host "Remote tag $tagName exists. Deleting..."
    git push origin --delete $tagName
}

$rawBase = 'https://raw.githubusercontent.com/fosterbarnes/res/refs/heads/main/btn'
$downloadsTable = @"
### Downloads

<table border="0">
<tbody>
<tr>
<td valign="top"><a href="https://github.com/fosterbarnes/musicApp/releases/download/$tagName/musicApp-${tagName}-${releaseTagSegment}-x64-installer.exe"><img src="$rawBase/x64Installer.svg" width="180" height="auto" alt="x64 installer"/></a></td>
<td valign="top"><a href="https://github.com/fosterbarnes/musicApp/releases/download/$tagName/musicApp-${tagName}-${releaseTagSegment}-x86-installer.exe"><img src="$rawBase/x86Installer.svg" width="180" height="auto" alt="x86 installer"/></a></td>
<td valign="top"><a href="https://github.com/fosterbarnes/musicApp/releases/download/$tagName/musicApp-${tagName}-${releaseTagSegment}-arm64-installer.exe"><img src="$rawBase/arm64.svg" width="180" height="auto" alt="arm64 installer"/></a></td>
</tr>
<tr>
<td valign="top"><a href="https://github.com/fosterbarnes/musicApp/releases/download/$tagName/musicApp-${tagName}-${releaseTagSegment}-portable.zip"><img src="$rawBase/portable.svg" width="180" height="auto" alt="portable .zip"/></a></td>
</tr>
</tbody>
</table>

### Release notes

"@

$finalReleaseNotes = $downloadsTable + $releaseNotes

runNativeCommand git @('tag', $tagName) 'git tag'
runNativeCommand git @('push', 'origin', $tagName) 'git push tag'
runNativeCommand gh @('release', 'create', $tagName, $finalPortable, $finalX64, $finalX86, $finalArm, '--title', $releaseName, '--notes', $finalReleaseNotes, '--latest') 'gh release create'
$releaseUrl = "$appURL/releases/tag/$tagName"
Write-Host "Released $tagName`: $releaseUrl"
if (-not $env:BASE_BUILD_PIPELINE) { openUrl $releaseUrl }

Remove-Item -Path $finalPortable, $finalX64, $finalX86, $finalArm, $portableZip -Recurse -Force -ErrorAction SilentlyContinue
closeOut 3
