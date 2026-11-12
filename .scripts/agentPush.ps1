#requires -Version 7.0
# The one-click push, for you (oc sidebar) and the agent-push skill. The default mode checks the bump
# and buildNotes.txt first, then runs prePush.ps1, push.ps1 and, while v<version> is not published
# ($agentPushRelease), pushRelease.ps1 in-process, and prints the report. -Context prints what is
# needed to write buildNotes.txt; -Tags prints 10 candidate version tags; -SetTag writes the picked one.
param([Alias('h')][switch]$Help, [switch]$Context, [switch]$Tags, [string]$SetTag)
$ErrorActionPreference = 'Stop'
if ($Help) {
    Write-Host @'
agentPush.ps1 [-Context | -Tags | -SetTag <tag>]
Release by hand: 1) bumpVersion.ps1 when app files changed, 2) write buildNotes.txt (line 1 as
printed, one blank line, - bullets for users), 3) agentPush.ps1 (checks, builds, commits, pushes, releases).
'@
    return
}
. "$PSScriptRoot\scriptHelper.ps1"
Set-Location -LiteralPath $repoRoot
if ($Tags) { getVersionTags 10; return }
if ($SetTag) {
    $set = setVersionTag $SetTag
    Write-Host "line 1: v$versionContents $set [<label> > <summary>]"
    return
}
$tagWord = requireVersionTag
$notesChanged = testBuildNotesChanged

# A published version is never released again, and shipped files must not change under it.
$published = (getReleaseState) -eq 'published'
$shipped = @(if ($published) { getShippedChangesSinceRelease })
$releasePlan = if (-not $agentPushRelease) { 'off' } elseif ($published) { 'exists' } else { 'on' }
$releaseText = switch ($releasePlan) {
    'on' { "on: publishes $releaseTag after the push" }
    'exists' { "$releaseTag already published, skipped" }
    default { 'off' }
}
$bumpFix = "App files changed since $releaseTag was published. Run .scripts\bumpVersion.ps1, update this " +
    "session's buildNotes.md heading and Version bullet, then rerun agentPush.ps1 -Context."

if ($Context) {
    Write-Host "branch: $(git branch --show-current)  version: $versionContents $tagWord"
    Write-Host "release: $releaseText"
    Write-Host "line 1: v$versionContents $tagWord [<label> > <summary>]"
    Write-Host "labels (decision order; rules in AGENTS.md 'buildNotes.txt' section): $($commitLabels -join ' ')"
    Write-Host '--- status'
    git -c core.safecrlf=false status --short 2>$null
    Write-Host '--- new buildNotes.md entries (dated after the last commit)'
    getNewBuildNotesEntries
    Write-Host '--- last 10 subjects'
    git log -10 --format=%s
    Write-Host '--- last commit message (style sample)'
    git log -1 --format=%B
    if ($notesChanged) { Write-Host 'WARN: buildNotes.txt already holds a new message; overwrite it.' }
    if ($shipped) { Write-Host "ACTION: $bumpFix" }
    return
}

# Fail on a bad message before the slow build runs.
if ($shipped) { throw "$bumpFix Changed: $($shipped -join ', ')" }
if (-not $notesChanged) { throw 'buildNotes.txt is unchanged since the last commit; write the new message first.' }
$lines = @([IO.File]::ReadAllLines($buildNotes))
if ($lines.Count -lt 3 -or $lines[1].Trim() -or -not ($lines[2..($lines.Count - 1)] -join '').Trim()) {
    throw 'buildNotes.txt needs line 1, one blank line, then a non-empty body.'
}
assertCommitSubject $lines[0].Trim() "v$versionContents $tagWord"

# Run each script in-process as one pipeline so closeOut and openUrl stay quiet; print the tail only on failure.
$previousPipeline = $env:BASE_BUILD_PIPELINE
try {
    $env:BASE_BUILD_PIPELINE = '1'
    $steps = @('prePush', 'push') + @(if ($releasePlan -eq 'on') { 'pushRelease' })
    foreach ($step in $steps) {
        Write-Host "$step.ps1 running..."
        $log = [Collections.Generic.List[string]]::new()
        try { & "$PSScriptRoot\$step.ps1" *>&1 | ForEach-Object { $log.Add("$_") } }
        catch {
            $log | Select-Object -Last 40
            throw "$step.ps1 failed: $_"
        }
        Write-Host "$step.ps1 passed."
    }
} finally {
    $env:BASE_BUILD_PIPELINE = $previousPipeline
}

Set-Location -LiteralPath $repoRoot
Write-Host "--- pushed $(git rev-parse --short=7 HEAD) to origin/$(git branch --show-current)"
git show -s --format=%B HEAD
git show --name-status --format= HEAD
switch ($releasePlan) {
    'on' { Write-Host "--- released $releaseTag`: $appURL/releases/tag/$releaseTag" }
    'exists' { Write-Host "--- $releaseTag already published; release skipped" }
}
