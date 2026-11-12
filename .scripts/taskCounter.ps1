#requires -Version 7.0
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\scriptHelper.ps1"
Set-Location -LiteralPath $repoRoot

function Clear-EmptyToDoSectionHeaders {
    param([string[]]$Lines)
    $lst = [System.Collections.Generic.List[string]]::new()
    foreach ($ln in @(if ($null -eq $Lines) { @() } else { @($Lines) })) {
        [void]$lst.Add($ln)
    }
    do {
        $changed = $false
        $idx = 0
        while ($idx -lt $lst.Count) {
            $t = $lst[$idx].TrimStart()
            if (-not $t.StartsWith('#')) { $idx++; continue }
            $m = [regex]::Match($t, '^#+')
            $L = $m.Value.Length
            if ($L -lt 2) { $idx++; continue }
            $endExclusive = $lst.Count
            for ($jj = $idx + 1; $jj -lt $lst.Count; $jj++) {
                $tt = $lst[$jj].TrimStart()
                if (-not $tt.StartsWith('#')) { continue }
                $nextL = ([regex]::Match($tt, '^#+')).Value.Length
                if ($nextL -le $L) { $endExclusive = $jj; break }
            }
            $hollow = $true
            for ($k = $idx + 1; $k -lt $endExclusive; $k++) {
                $tt = $lst[$k].Trim()
                if ($tt.Length -eq 0) { continue }
                if ($tt.StartsWith('#')) { continue }
                $hollow = $false
                break
            }
            if ($hollow) {
                $lst.RemoveRange($idx, $endExclusive - $idx)
                $changed = $true
                continue
            }
            $idx++
        }
    } while ($changed)
    @($lst.ToArray())
}

$lines = @(Get-Content -LiteralPath $tasks -Encoding UTF8)
$todoOut = [System.Collections.ArrayList]::new()
[void]$todoOut.Add('# ToDo')
[void]$todoOut.Add('')
$total = $completed = $notDone = 0
foreach ($line in $lines) {
    $ts = $line.TrimStart()
    if ($ts.StartsWith('#')) { [void]$todoOut.Add($line); continue }
    if ($line.Trim().Length -eq 0) {
        if ($todoOut.Count -ge 2) {
            $l = [string]$todoOut[$todoOut.Count - 1]
            if ($l -ne '' -and ($l.TrimStart().StartsWith('#') -or $l -match '\S')) { [void]$todoOut.Add('') }
        }
        continue
    }
    if (-not ($ts -match '\S')) { continue }
    $total++
    if ($line.Contains('~')) { $completed++; continue }
    [void]$todoOut.Add($line)
    $notDone++
}

$pct = if ($total -eq 0) { 0 } else { [math]::Round(100 * $completed / $total, 1) }
Write-Host "$completed/$total tasks completed ($pct%)"
if ($notDone -eq 0) {
    $todoOut = @('# ToDo', '', '_No undone tasks._')
} else {
    $todoOut = [Collections.ArrayList]@(Clear-EmptyToDoSectionHeaders @($todoOut))
}

$nl = [Environment]::NewLine
$todoPath = "$repoRoot\.md\ToDo.md"
writeFileNoBom -LiteralPath $todoPath -Content ((@($todoOut) -join $nl) + $nl)

$newReadme = @(Get-Content -LiteralPath $readme -Encoding UTF8)
$pctInt = [int][math]::Round($pct)
for ($i = 0; $i -lt $newReadme.Count; $i++) {
    if ($newReadme[$i] -match 'progress-bars\.entcheneric\.com.*progress=\d+') { $newReadme[$i] = $newReadme[$i] -replace 'progress=\d+', "progress=$pctInt" }
    if ($newReadme[$i] -match '^\*\*\d+ / \d+ tasks complete \(\d+.*%\)\*\*') { $newReadme[$i] = "**$completed / $total tasks complete ($pct%)**" }
}
while ($newReadme.Count -gt 0 -and $newReadme[$newReadme.Count - 1] -match '^\s*$') {
    if ($newReadme.Count -eq 1) { $newReadme = @(); break }
    $newReadme = $newReadme[0..($newReadme.Count - 2)]
}
writeFileNoBom -LiteralPath $readme -Content $(if ($newReadme.Count -eq 0) { '' } else { ($newReadme -join $nl) + $nl })
