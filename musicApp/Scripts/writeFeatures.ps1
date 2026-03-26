
param(
    [string]$TasksPath = (Join-Path $PSScriptRoot '..\..\.md\Tasks.md'),
    [string]$FeaturesPath = (Join-Path $PSScriptRoot '..\..\.md\Features.md'),
    [string]$ReadmePath = (Join-Path $PSScriptRoot '..\..' 'README.md')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path -Path $PSScriptRoot -ChildPath 'mdSyncHelper.ps1')

$taskRaw = Get-Content -LiteralPath $TasksPath -Raw -Encoding UTF8
$featureRaw = Get-Content -LiteralPath $FeaturesPath -Raw -Encoding UTF8
$taskLines = if ([string]::IsNullOrEmpty($taskRaw)) { @() } else { @($taskRaw -split '\r?\n') }
$featureLines = if ([string]::IsNullOrEmpty($featureRaw)) { @() } else { @($featureRaw -split '\r?\n') }
$taskLines = Trim-TrailingBlankLines $taskLines
$featureLines = Trim-TrailingBlankLines $featureLines

$renderedLines = Merge-FeaturesFromTasks -TaskLines $taskLines -FeatureLines $featureLines
$renderedLines = Trim-TrailingBlankLines $renderedLines

$settingsAlreadyInFeatures = $false
foreach ($line in $featureLines) {
    if ($line -match '^\s*##\s+Settings Menu\s*$') {
        $settingsAlreadyInFeatures = $true
        break
    }
}

if (-not $settingsAlreadyInFeatures) {
    $settingsLines = @(Get-MarkdownTopLevelSectionLines -Lines $taskLines -SectionTitle 'Settings Menu')
    if ($settingsLines.Count -gt 0) {
        $renderedLines = @($renderedLines; ''; $settingsLines)
        $renderedLines = Trim-TrailingBlankLines $renderedLines
    }
}

$newFeatureText = (($renderedLines -join "`r`n") -replace '~~', '')

Write-Host "`nUpdating Features.md..." -ForegroundColor Yellow
Set-Content -LiteralPath $FeaturesPath -Value $newFeatureText -Encoding UTF8
Write-Host "Done."

$featuresUpdatedLines = @($newFeatureText -split '\r?\n')
$readmeRaw = Get-Content -LiteralPath $ReadmePath -Raw -Encoding UTF8
$readmeLines = if ([string]::IsNullOrEmpty($readmeRaw)) { @() } else { @($readmeRaw -split '\r?\n') }
$newReadmeLines = Sync-ReadmeGeneralUsageFromFeatures -ReadmeLines $readmeLines -FeaturesUpdatedLines $featuresUpdatedLines
$newReadmeLines = Trim-TrailingBlankLines $newReadmeLines

Write-Host "`nUpdating README.md..." -ForegroundColor Yellow
Set-Content -LiteralPath $ReadmePath -Value ($newReadmeLines -join "`r`n") -Encoding UTF8
Write-Host "Done."

