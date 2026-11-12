#requires -Version 7.0
# Regenerates Features.md from Tasks.md (README is not modified).
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\scriptHelper.ps1"
Set-Location -LiteralPath $repoRoot

$newLine = [Environment]::NewLine
$taskLines = Trim-TrailingBlankLines (Normalize-MarkdownLineArray -Lines $tasksRaw)
$featureLines = Trim-TrailingBlankLines (Normalize-MarkdownLineArray -Lines $featuresRaw)
$renderedLines = Trim-TrailingBlankLines (Merge-FeaturesFromTasks -TaskLines $taskLines -FeatureLines $featureLines)

if (-not ($featureLines -match '^\s*##\s+Settings Menu\s*$')) {
    $settingsLines = @(Get-MarkdownTopLevelSectionLines -Lines $taskLines -SectionTitle 'Settings Menu')
    if ($settingsLines.Count -gt 0) {
        $renderedLines = Trim-TrailingBlankLines @($renderedLines; ''; $settingsLines)
    }
}

$newFeatureText = (($renderedLines -join $newLine) -replace '~~', '')
writeFileNoBom -LiteralPath $features -Content ($newFeatureText + $newLine)
