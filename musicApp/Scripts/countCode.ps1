Set-Location "$PSScriptRoot\..\.."
$repoRoot = $PWD.Path
$outPath = Join-Path $repoRoot ".md\scc.txt"
New-Item -ItemType Directory -Path (Split-Path $outPath) -Force | Out-Null
scc . --no-size --no-complexity --no-cocomo --ci | Out-File -FilePath $outPath -Encoding utf8

$totalLines = $null
foreach ($line in Get-Content -Path $outPath) {
    if ($line -match '^\s*Total\s+\d+\s+([\d,]+)') {
        $totalLines = $Matches[1]
        break
    }
}
if (-not $totalLines) {
    Write-Error "Could not parse Total Lines from scc output: $outPath"
    exit 1
}

$readmePath = Join-Path $repoRoot "README.md"
$readme = Get-Content -Raw -Path $readmePath
$pattern = '(?m)^\[([\d,]+)\](\(https://github\.com/fosterbarnes/musicApp/blob/main/\.md/scc\.txt\)\s+lines of code and counting\.\.\.)'
$updated = [regex]::Replace($readme, $pattern, { param($m) '[' + $totalLines + ']' + $m.Groups[2].Value })
if ($updated -eq $readme) {
    Write-Error "README.md: no scc.txt link line matching '[N](.../scc.txt) lines of code and counting...'"
    exit 1
}
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($readmePath, $updated, $utf8NoBom)
