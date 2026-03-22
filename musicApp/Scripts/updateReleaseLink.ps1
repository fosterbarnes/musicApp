#Requires -Version 5.1

param(
    [string] $ReadmePath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'README.md'),
    [string] $RepoOwner = 'fosterbarnes',
    [string] $RepoName = 'musicApp'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$basePath = (Get-Item $PSScriptRoot).Parent.FullName
$versionFile = Join-Path $basePath 'Version'
$versionTagFile = Join-Path $basePath 'VersionTag'
$version = (Get-Content -LiteralPath $versionFile -First 1 -Encoding UTF8).Trim()
$versionTag = (Get-Content -LiteralPath $versionTagFile -First 1 -Encoding UTF8).Trim()
$tagName = "v$version"
$zipFileName = "musicApp_v${version}_${versionTag}-portable.zip"
$zipPath = "$tagName/$zipFileName"
$releaseUrl = "https://github.com/$RepoOwner/$RepoName/releases/download/$zipPath"
$readme = Get-Content -LiteralPath $ReadmePath -Raw -Encoding UTF8
$ownerRepoEscaped = [regex]::Escape("$RepoOwner/$RepoName")
$oldLinkRegex = '\[download the latest release\]\(https://github\.com/' + $ownerRepoEscaped + '/releases/download/v[^)]+\)'

$matchCount = ([regex]::Matches($readme, $oldLinkRegex)).Count
if ($matchCount -lt 1) {
    throw "Could not find the expected release link in README: $ReadmePath"
}

$updated = [regex]::Replace(
    $readme,
    $oldLinkRegex,
    "[download the latest release]($releaseUrl)"
)
Write-Host "`nUpdating release link in README..." -ForegroundColor Yellow
Write-Host "$releaseUrl"
Set-Content -LiteralPath $ReadmePath -Encoding UTF8 -Value $updated
Write-Host "Done."
