#Requires -Version 5.1

param(
    [string] $Path = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'Tasks.md')
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

if ($total -eq 0) {
    Write-Host '0/0 tasks (no task lines found)'
    exit 0
}

$pct = [math]::Round(100 * $completed / $total, 1)
Write-Host "$completed/$total tasks completed ($pct%)"
