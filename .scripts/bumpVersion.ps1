#requires -Version 7.0
# Bumps .version\version line 1 (patch unless -Minor or -Major) and rolls a new version tag on line 2;
# line 3 (last build platform) stays. -RerollTag changes only the tag. Run once per release, when
# shipped files changed (AGENTS.md Revision). No closeOut: the printout is what you need next.
param([Alias('h')][switch]$Help, [switch]$Minor, [switch]$Major, [switch]$RerollTag)
$ErrorActionPreference = 'Stop'
if ($Help) { Write-Host 'bumpVersion.ps1 [-Minor | -Major | -RerollTag]'; return }
if ([int]$Minor.IsPresent + [int]$Major.IsPresent + [int]$RerollTag.IsPresent -gt 1) {
    throw 'Use only one of -Minor, -Major, -RerollTag.'
}
. "$PSScriptRoot\scriptHelper.ps1"
Set-Location -LiteralPath $repoRoot
if ($RerollTag) {
    Write-Host "Version tag $(setVersionTag (getVersionTags 1))"
} else {
    $part = if ($Major) { 'Major' } elseif ($Minor) { 'Minor' } else { 'Patch' }
    Write-Host (bumpVersion $part)
}
$lines = readVerFile
Write-Host "line 1: v$($lines[0]) $($lines[1]) [<label> > <summary>]"
