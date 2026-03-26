$musicAppRoot = Split-Path $PSScriptRoot -Parent
$versionBuildPath = Join-Path $musicAppRoot "VersionBuild"
$updaterDir = Join-Path $musicAppRoot ".updater"
$swapDir = Join-Path $musicAppRoot ".updater.swap"
$csproj = Join-Path $updaterDir "musicApp.Updater.csproj"
$swapCsproj = Join-Path $swapDir "musicApp.Updater.Swap.csproj"
$tfm = "net8.0-windows"
$cfg = "Release"

$updaterArtifacts = @(
    "musicApp-updater.exe",
    "musicApp-updater.pdb",
    "musicApp-updater.runtimeconfig.json",
    "Version",
    "VersionBuild",
    "VersionTag",
    "musicApp-updater.deps.json",
    "musicApp-updater.dll"
)

function Publish-SwapExeTo {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DestDir,
        [Parameter(Mandatory = $true)]
        [ValidateSet("win-x86", "win-x64")]
        [string]$RuntimeId
    )

    if (-not (Test-Path -LiteralPath $swapCsproj)) {
        throw "Swap project not found: $swapCsproj"
    }

    $publishDir = Join-Path $env:TEMP ("musicApp-swap-publish-" + [Guid]::NewGuid().ToString("n"))
    try {
        dotnet publish $swapCsproj -c Release -r $RuntimeId --self-contained false `
            -p:PublishSingleFile=true `
            -o $publishDir
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish swap failed with exit $LASTEXITCODE" }

        $built = Join-Path $publishDir "musicApp-updater-swap.exe"
        if (-not (Test-Path -LiteralPath $built)) {
            throw "Swap output not found: $built"
        }
        New-Item -ItemType Directory -Force -Path $DestDir | Out-Null
        # FDD single-file may emit the exe plus sidecar json/pdb; copy entire publish folder root.
        Get-ChildItem -LiteralPath $publishDir -File | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $DestDir $_.Name) -Force
        }
    }
    finally {
        Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Copy-UpdaterOutputToMusicAppBin {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("x86", "x64", "AnyCPU")]
        [string]$Platform
    )

    $relativeOut = switch ($Platform) {
        "x64" { "bin\x64\$cfg\$tfm" }
        "x86" { "bin\x86\$cfg\$tfm" }
        "AnyCPU" { "bin\$cfg\$tfm" }
    }
    $srcDir = Join-Path $updaterDir $relativeOut
    $destDir = Join-Path $musicAppRoot $relativeOut

    if (-not (Test-Path $srcDir)) {
        throw "Updater output not found: $srcDir"
    }

    New-Item -ItemType Directory -Force -Path $destDir | Out-Null

    foreach ($name in $updaterArtifacts) {
        $src = Join-Path $srcDir $name
        if (-not (Test-Path -LiteralPath $src)) {
            throw "Missing updater artifact: $src"
        }
        Copy-Item -LiteralPath $src -Destination (Join-Path $destDir $name) -Force
    }

    $swapRid = switch ($Platform) {
        "x86" { "win-x86" }
        "x64" { "win-x64" }
        "AnyCPU" { "win-x64" }
    }
    Publish-SwapExeTo -DestDir $destDir -RuntimeId $swapRid

    Write-Host "Copied updater + swap -> $destDir" -ForegroundColor DarkGray
}

Write-Host "Building musicApp-updater for x86..." -ForegroundColor Yellow
[System.IO.File]::WriteAllText($versionBuildPath, "x86")
dotnet build $csproj -c Release -p:Platform=x86
Copy-UpdaterOutputToMusicAppBin -Platform x86

Write-Host "`nBuilding musicApp-updater for x64..." -ForegroundColor Yellow
[System.IO.File]::WriteAllText($versionBuildPath, "x64")
dotnet build $csproj -c Release -p:Platform=x64
Copy-UpdaterOutputToMusicAppBin -Platform x64

Write-Host "`nBuilding musicApp-updater for AnyCPU..." -ForegroundColor Yellow
[System.IO.File]::WriteAllText($versionBuildPath, "portable")
dotnet build $csproj -c Release -p:Platform=AnyCPU
Copy-UpdaterOutputToMusicAppBin -Platform AnyCPU

Write-Host "`nEach platform: updater under .updater\ -> musicApp bin; swap = framework-dependent single-file (needs .NET 8 runtime like the main app)." -ForegroundColor DarkGray
