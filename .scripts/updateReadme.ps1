#requires -Version 7.0
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\scriptHelper.ps1"
Set-Location -LiteralPath $repoRoot
if (-not (Test-Path -LiteralPath $readme)) { throw "README not found: $readme" }
$tagName = "v$versionContents"
$zipFileName = "musicApp-v${versionContents}-${versionTagContents}-portable.zip"
$releaseUrl = "https://github.com/fosterbarnes/musicApp/releases/download/$tagName/$zipFileName"
$oldLinkRegex = '\[download the latest release\]\(https://github\.com/' + [regex]::Escape('fosterbarnes/musicApp') + '/releases/download/v[^)]+\)'
$semverVRe = '\bv\d+\.\d+\.\d+\b'
$tagSlugRe = '(musicApp-' + [regex]::Escape($tagName) + '-)[^-]+-(x64-installer\.exe|x86-installer\.exe|arm64-installer\.exe|portable\.zip)'
$text = [IO.File]::ReadAllText($readme)
$text = [regex]::Replace($text, $semverVRe, $tagName)
$text = [regex]::Replace($text, $tagSlugRe, '${1}' + $versionTagContents + '-${2}')
$updated = [regex]::Replace($text, $oldLinkRegex, "[download the latest release]($releaseUrl)").TrimEnd()
writeFileNoBom -LiteralPath $readme -Content $updated
closeOut 3
