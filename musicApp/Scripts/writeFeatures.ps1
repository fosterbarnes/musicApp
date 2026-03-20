
param(
    [string]$TasksPath = (Join-Path $PSScriptRoot '..\..\.md\Tasks.md'),
    [string]$FeaturesPath = (Join-Path $PSScriptRoot '..\..\.md\Features.md'),
    [string]$ReadmePath = (Join-Path $PSScriptRoot '..\..' 'README.md')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path -Path $PSScriptRoot -ChildPath 'MarkdownSyncHelpers.ps1')

$taskLines = @((Get-Content -LiteralPath $TasksPath -Raw -Encoding UTF8) -split "\r?\n")
$featureLines = @((Get-Content -LiteralPath $FeaturesPath -Raw -Encoding UTF8) -split "\r?\n")

$renderedLines = Merge-FeaturesFromTasks -TaskLines $taskLines -FeatureLines $featureLines
$newFeatureText = (($renderedLines -join "`r`n") -replace '~~', '')

Set-Content -LiteralPath $FeaturesPath -Value $newFeatureText -Encoding UTF8
Write-Host "Updated .md\Features.md from .md\Tasks.md (copied ~~items, stripped ~~)."

$featuresUpdatedLines = @($newFeatureText -split "\r?\n")
$readmeLines = @((Get-Content -LiteralPath $ReadmePath -Raw -Encoding UTF8) -split "\r?\n")
$newReadmeLines = Sync-ReadmeGeneralUsageFromFeatures -ReadmeLines $readmeLines -FeaturesUpdatedLines $featuresUpdatedLines

Set-Content -LiteralPath $ReadmePath -Value ($newReadmeLines -join "`r`n") -Encoding UTF8
Write-Host "Synced README.md '# General Usage Info' from .md\Features.md."

