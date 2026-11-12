#requires -Version 7.0
param([Alias('f')][switch]$Force, [Alias('n')][switch]$DryRun, [Parameter(Position = 0)][string]$Message)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\scriptHelper.ps1"
Set-Location -LiteralPath $repoRoot
# buildNotes.txt holds the next commit: line 1 subject, one blank line, then the body. A passed message
# wins. The file counts only when changed since the last commit; unchanged, it is the last push's message.
$subject = $Message.Trim()
$body = ''
if (-not $subject -and (testBuildNotesChanged) -and (Test-Path -LiteralPath $buildNotes)) {
    $lines = @([IO.File]::ReadAllLines($buildNotes))
    if ($lines.Count -gt 0 -and $lines[0].Trim()) {
        $subject = $lines[0].Trim()
        if ($lines.Count -ge 2 -and $lines[1].Trim()) {
            throw 'buildNotes.txt must have one blank line after the first line, then the commit description.'
        }
        if ($lines.Count -gt 2) { $body = ($lines[2..($lines.Count - 1)] -join "`n").Trim() }
    }
}
if (-not $subject -and -not $DryRun) { $subject = (Read-Host 'Commit message').Trim() }
if (-not $subject) { throw 'Provide a commit message, or write a new one to buildNotes.txt (it is unchanged since the last commit).' }
$branch = ((& git branch --show-current) | Out-String).Trim()
if ($LASTEXITCODE) { throw 'Could not determine the current branch.' }
if (-not $branch) { throw 'Detached HEAD; refusing to push.' }
if ($DryRun) {
    Write-Host 'Dry run: git add -A'
    Write-Host "Dry run: git commit -m `"$subject`"$(if ($body) { " -m <$($body.Length) chars from buildNotes.txt>" })"
    Write-Host "Dry run: git push origin $branch$(if ($Force) { ' --force' })"
    if (-not $env:BASE_BUILD_PIPELINE) { Write-Host "Dry run: open $appURL" }
    return
}
runNativeCommand git @('add', '-A') 'git add'
$commitArgs = @('commit', '-m', $subject)
if ($body) { $commitArgs += @('-m', $body) }
runNativeCommand git $commitArgs 'git commit'
$pushArgs = @('push', 'origin', $branch); if ($Force) { $pushArgs += '--force' }
runNativeCommand git $pushArgs 'git push'
if (-not $env:BASE_BUILD_PIPELINE) { openUrl $appURL }
closeOut 3
