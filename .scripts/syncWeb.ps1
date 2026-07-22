. "$PSScriptRoot\scriptHelper.ps1"

$siteIndex = "$root\musicApp.info\index.html"
if (-not (Test-Path -LiteralPath $siteIndex)) {
    throw "Website index not found: $siteIndex"
}

$tag = "v$($versionContents.Trim())"
$segment = Get-ReleaseTagSegment -Tag $versionTagContents
$base = "https://github.com/fosterbarnes/musicApp/releases/download/$tag/musicApp-$tag-$segment"
$downloads = @(
    [pscustomobject]@{ Label = 'Download x64 installer (.exe)'; Url = "$base-x64-installer.exe" }
    [pscustomobject]@{ Label = 'Download x86 installer (.exe)'; Url = "$base-x86-installer.exe" }
    [pscustomobject]@{ Label = 'Download ARM64 installer (.exe)'; Url = "$base-arm64-installer.exe" }
    [pscustomobject]@{ Label = 'Download portable (.zip)'; Url = "$base-portable.zip" }
)

$html = [System.IO.File]::ReadAllText($siteIndex)
$originalHtml = $html
foreach ($download in $downloads) {
    $suffix = '" aria-label="' + $download.Label + '"'
    $pattern = '(?<=<a class="home-download-btn" href=")[^"]+(?=' + [regex]::Escape($suffix) + ')'
    $match = [regex]::Match($html, $pattern)
    if (-not $match.Success) {
        throw "Website download link not found: $($download.Label)"
    }

    $html = $html.Remove($match.Index, $match.Length).Insert($match.Index, $download.Url)
}

if ($html -ne $originalHtml) {
    Write-RepoUtf8NoBomFile -LiteralPath $siteIndex -Content $html
    Write-Host 'Synced website download links.' -ForegroundColor Green
}
