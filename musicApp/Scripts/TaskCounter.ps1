#Requires -Version 5.1

param(
    [string] $Path = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) '.md\Tasks.md'),
    [string] $ReadmePath = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'README.md'),
    [string] $ToDoPath = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) '.md\ToDo.md')
)
function Test-IsTaskLine([string] $line) {
    $t = $line.TrimStart()
    return $t.Length -gt 0 -and -not $t.StartsWith('#') -and $t -match '\S'
}

Write-Host "`nCounting tasks..." -ForegroundColor Yellow

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
Write-Host "$completed/$total tasks completed ($pct%)"
Write-Host "`nUpdating ToDo.md..." -ForegroundColor Yellow

$todoOut = [System.Collections.ArrayList]::new()
[void]$todoOut.Add('# ToDo')
[void]$todoOut.Add('')

$notDone = 0
foreach ($line in $lines) {
    $trimStart = $line.TrimStart()
    if ($trimStart.StartsWith('#')) {
        [void]$todoOut.Add($line)
        continue
    }
    if ($line.Trim().Length -eq 0) {
        if ($todoOut.Count -lt 2) { continue }
        $last = [string]$todoOut[$todoOut.Count - 1]
        if ($last -eq '') { continue }
        if ($last.TrimStart().StartsWith('#')) { [void]$todoOut.Add('') }
        elseif ($last -match '\S') { [void]$todoOut.Add('') }
        continue
    }
    if (-not (Test-IsTaskLine $line)) { continue }
    if ($line.Contains('~')) { continue }
    [void]$todoOut.Add($line)
    $notDone++
}

if ($notDone -eq 0) {
    $todoOut = @('# ToDo', '', '_No undone tasks._')
}
$todoOut | Set-Content -LiteralPath $ToDoPath -Encoding UTF8
Write-Host "Done."
Write-Host "`nUpdating README.md..." -ForegroundColor Yellow

$newReadme = @((Get-Content -LiteralPath $ReadmePath -Encoding UTF8))
$pctInt = [int][math]::Round($pct)
for ($i = 0; $i -lt $newReadme.Count; $i++) {
    if ($newReadme[$i] -match 'progress-bars\.entcheneric\.com.*progress=\d+') {
        $newReadme[$i] = $newReadme[$i] -replace 'progress=\d+', "progress=$pctInt"
    }
    if ($newReadme[$i] -match '^\*\*\d+ / \d+ tasks complete \(\d+.*%\)\*\*') {
        $newReadme[$i] = "**$completed / $total tasks complete ($pct%)**"
    }
}

while ($newReadme.Count -gt 0 -and $newReadme[$newReadme.Count - 1] -match '^\s*$') {
    if ($newReadme.Count -eq 1) { $newReadme = @(); break }
    $newReadme = $newReadme[0..($newReadme.Count - 2)]
}
$newReadme | Set-Content -LiteralPath $ReadmePath -Encoding UTF8
Write-Host "Done."