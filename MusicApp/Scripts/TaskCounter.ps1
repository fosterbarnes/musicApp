#Requires -Version 5.1

param(
    [string] $Path = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) '.md\Tasks.md'),
    [string] $ReadmePath = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'README.md')
)

if (-not (Test-Path -LiteralPath $Path)) {
    Write-Error "Tasks file not found: $Path"
    exit 1
}

$lines = Get-Content -LiteralPath $Path -Encoding UTF8
$taskLines = $lines | Where-Object {
    $trimmed = $_.TrimStart()
    $trimmed.Length -gt 0 -and
    -not $trimmed.StartsWith('#') -and
    $trimmed -match '\S'
}

$total = $taskLines.Count
$completed = ($taskLines | Where-Object { $_.Contains('~') }).Count

$pct = if ($total -eq 0) { 0 } else { [math]::Round(100 * $completed / $total, 1) }
if ($total -eq 0) {
    Write-Host '0/0 tasks (no task lines found)'
} else {
    Write-Host "$completed/$total tasks completed ($pct%)"
}

# Sync README.md: replace content between <summary>Tasks</summary> and </details> with full .md\Tasks.md
if (-not (Test-Path -LiteralPath $ReadmePath)) {
    Write-Error "README file not found: $ReadmePath"
    exit 1
}

$readmeLines = Get-Content -LiteralPath $ReadmePath -Encoding UTF8
$summaryIndex = -1
$detailsIndex = -1
for ($i = 0; $i -lt $readmeLines.Count; $i++) {
    if ($readmeLines[$i] -match '<summary>Tasks</summary>') { $summaryIndex = $i }
    if ($readmeLines[$i] -eq '</details>') { $detailsIndex = $i; break }
}

if ($summaryIndex -lt 0 -or $detailsIndex -lt 0) {
    Write-Error "README.md: could not find <summary>Tasks</summary> or </details>"
    exit 1
}

$tasksContent = Get-Content -LiteralPath $Path -Encoding UTF8
$newReadme = $readmeLines[0..$summaryIndex] + $tasksContent + $readmeLines[$detailsIndex..($readmeLines.Count - 1)]

# Update progress bar URL (progress=N) and task summary line in README
$pctInt = [int][math]::Round($pct)
for ($i = 0; $i -lt $newReadme.Count; $i++) {
    if ($newReadme[$i] -match 'progress-bars\.entcheneric\.com.*progress=\d+') {
        $newReadme[$i] = $newReadme[$i] -replace 'progress=\d+', "progress=$pctInt"
    }
    if ($newReadme[$i] -match '^\*\*\d+ / \d+ tasks complete \(\d+.*%\)\*\*') {
        $newReadme[$i] = "**$completed / $total tasks complete ($pct%)**"
    }
}

$newReadme | Set-Content -LiteralPath $ReadmePath -Encoding UTF8
