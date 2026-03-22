Write-Host "Running pre-push tasks..." -ForegroundColor Yellow
& (Join-Path -Path $PSScriptRoot -ChildPath 'build.ps1')
& (Join-Path -Path $PSScriptRoot -ChildPath 'taskCounter.ps1')
& (Join-Path -Path $PSScriptRoot -ChildPath 'writeFeatures.ps1')
& (Join-Path -Path $PSScriptRoot -ChildPath 'updateReleaseLink.ps1')