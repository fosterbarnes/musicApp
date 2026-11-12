#requires -Version 7.0
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Path $PSScriptRoot -Parent
$projectName = 'musicApp'
$projectExeName = "$projectName.exe"
$appRoot = "$repoRoot\musicApp"
$csproj = "$appRoot\musicApp.csproj"
$updaterCsproj = "$appRoot\.updater\musicApp.Updater.csproj"
$updaterSwapCsproj = "$appRoot\.updater.swap\musicApp.Updater.Swap.csproj"
$dotnetFramework = 'net10.0-windows'
$tasks = "$repoRoot\.md\Tasks.md"
$features = "$repoRoot\.md\Features.md"
$siteIndex = "$repoRoot\musicApp.info\index.html"
$yapWords = "$repoRoot\.res\exe\YapBot.json"
$versionFolder = "$repoRoot\.version"
$version = "$versionFolder\version"
$versionBuild = "$versionFolder\versionBuild"
$buildNotes = "$repoRoot\buildNotes.txt"
$readme = "$repoRoot\README.md"
$installerFolder = "$repoRoot\.installer"
$installerOutput = "$installerFolder\Output"
$publishFolder = "$repoRoot\publish"
$iconDir = "$repoRoot\.res\icon"
$iconPng = "$iconDir\musicApp Icon.png"
$iconIco = "$iconDir\musicApp Icon.ico"
$wizardSmallPng = "$iconDir\installer-wizard-small.png"
$wizardLargePng = "$iconDir\installer-wizard-large.png"
$appPublisher = 'fosterbarnes'
$appURL = "https://github.com/$appPublisher/$projectName"
$ghRepo = "$appPublisher/$projectName"
# agentPush.ps1 publishes the GitHub release after the push unless v<version> is already published.
$agentPushRelease = $true
# Commit subject labels in decision order; AGENTS.md "buildNotes.txt" defines each.
$commitLabels = 'fix', 'feat.', 'patch', 'perf.', 'deps.', 'scripts', 'cleanup', 'docs', 'misc.'
# Paths that end up in the app, updater, or installers (the csproj and .iss includes). Keep in step
# with them: a change here under a published version needs bumpVersion.ps1.
$shippedPaths = '^(musicApp/|\.installer/|\.res/(icon|svg)/)'
$imageSizes = @(16, 32, 48, 64, 128, 256)
$pngInstallerSizes = @(512, 256, 128, 64, 32)
$buildTargets = @(
    @{ Architecture = 'x86'; RuntimeIdentifier = 'win-x86'; BinFolder = "$appRoot\bin\x86\Release\net10.0-windows"; ExePath = "$appRoot\bin\x86\Release\net10.0-windows\$projectExeName"; InstallerName = 'musicApp-x86-installer.exe'; InstallerScript = "$installerFolder\musicApp.x86.installer.iss" }
    @{ Architecture = 'x64'; RuntimeIdentifier = 'win-x64'; BinFolder = "$appRoot\bin\x64\Release\net10.0-windows"; ExePath = "$appRoot\bin\x64\Release\net10.0-windows\$projectExeName"; InstallerName = 'musicApp-x64-installer.exe'; InstallerScript = "$installerFolder\musicApp.x64.installer.iss" }
    @{ Architecture = 'arm64'; RuntimeIdentifier = 'win-arm64'; BinFolder = "$appRoot\bin\arm64\Release\net10.0-windows"; ExePath = "$appRoot\bin\arm64\Release\net10.0-windows\$projectExeName"; InstallerName = 'musicApp-arm64-installer.exe'; InstallerScript = "$installerFolder\musicApp.arm64.installer.iss" }
)
$noBom = New-Object System.Text.UTF8Encoding $false
$weztermExe = (Get-Command wezterm.exe -ErrorAction SilentlyContinue)?.Source

function readVerFile {
    param([string]$LiteralPath = $version)
    $lines = @(([IO.File]::ReadAllText($LiteralPath) -split '\r?\n' | ForEach-Object { $_.Trim() }))
    while ($lines.Count -lt 3) { $lines += '' }
    $lines
}

function writeFileNoBom {
    param([Parameter(Mandatory)][string]$LiteralPath, [Parameter(Mandatory)][string]$Content)
    [IO.File]::WriteAllText($LiteralPath, $Content, $noBom)
}

function writeVerFile {
    param(
        [Parameter(Mandatory)][string]$SemVer,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Tag,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Build
    )
    writeFileNoBom -LiteralPath $version -Content (($SemVer.Trim(), $Tag.Trim(), $Build.Trim()) -join "`n")
}

function setVerBuild {
    param([Parameter(Mandatory)][string]$Platform)
    writeFileNoBom -LiteralPath $versionBuild -Content ($Platform.Trim() + "`n")
}

function setVerBuildPlatform {
    param([Parameter(Mandatory)][string]$Platform)
    $lines = readVerFile
    writeVerFile -SemVer $lines[0] -Tag $lines[1] -Build $Platform
    setVerBuild $Platform
    $script:versionBuildContents = $Platform.Trim()
}

function requireVersionTag {
    $tagWord = (readVerFile)[1]
    if ($tagWord -cnotmatch '^[a-z]+[A-Z][a-z]*$') {
        throw "Invalid version tag '$tagWord' on line 2 of $version`: run .scripts\bumpVersion.ps1 -RerollTag."
    }
    $tagWord
}

function setVersionTag {
    param([Parameter(Mandatory)][string]$Tag)
    if ($Tag -cnotmatch '^[a-z]+[A-Z][a-z]*$') { throw "Invalid tag '$Tag': expected one camelCase two-word tag" }
    $lines = readVerFile
    writeVerFile -SemVer $lines[0] -Tag $Tag -Build $lines[2]
    $script:versionTagContents = $Tag
    $Tag
}

# Returns $Count distinct random camelCase two-word tags without writing anything.
function getVersionTags {
    param([int]$Count = 1)
    if (-not (Test-Path -LiteralPath $yapWords)) { throw "Missing word list $yapWords (private, gitignored)." }
    $raw = Get-Content -LiteralPath $yapWords -Raw | ConvertFrom-Json
    $words = @($raw | Where-Object { $_ -match '^[A-Za-z]+$' })
    if (-not $words.Count) { throw "No usable words found in $yapWords." }
    $tags = [Collections.Generic.HashSet[string]]::new()
    while ($tags.Count -lt $Count) {
        $first = $words[[Security.Cryptography.RandomNumberGenerator]::GetInt32($words.Count)]
        $second = $words[[Security.Cryptography.RandomNumberGenerator]::GetInt32($words.Count)]
        [void]$tags.Add($first.ToLowerInvariant() + $second.Substring(0, 1).ToUpperInvariant() + $second.Substring(1).ToLowerInvariant())
    }
    @($tags)
}

# Bumps line 1 (lower parts zeroed), rolls a new tag on line 2, keeps line 3.
function bumpVersion {
    param([ValidateSet('Patch', 'Minor', 'Major')][string]$Part = 'Patch')
    $lines = readVerFile
    $current = $lines[0]
    if ($current -notmatch '^(\d+)\.(\d+)\.(\d+)$') { throw "Invalid version '$current' on line 1 of $version" }
    $major, $minor, $patch = [int]$Matches[1], [int]$Matches[2], [int]$Matches[3]
    $next = switch ($Part) {
        'Major' { "$($major + 1).0.0" }
        'Minor' { "$major.$($minor + 1).0" }
        default { "$major.$minor.$($patch + 1)" }
    }
    $tagWord = getVersionTags 1
    writeVerFile -SemVer $next -Tag $tagWord -Build $lines[2]
    "$current -> $next ($tagWord)"
}

# 'published' or 'missing' for the GitHub release v<version>. Anything else (offline, auth) throws,
# so a push never runs on a guess.
function getReleaseState {
    $output = (& gh release view $releaseTag --repo $ghRepo --json tagName 2>&1 | Out-String).Trim()
    if (-not $LASTEXITCODE) { return 'published' }
    if ($output -match 'release not found') { return 'missing' }
    throw "gh release view $releaseTag failed (exit $LASTEXITCODE): $output"
}

# Shipped files ($shippedPaths) that differ from what the published v<version> shipped, including
# uncommitted and untracked ones.
function getShippedChangesSinceRelease {
    & git -C $repoRoot rev-parse --verify --quiet "refs/tags/$releaseTag" *> $null
    if ($LASTEXITCODE) { throw "Tag $releaseTag is not in this clone: run git fetch --tags." }
    $changed = @(& git -C $repoRoot -c core.safecrlf=false diff --name-only "refs/tags/$releaseTag" --)
    if ($LASTEXITCODE) { throw "git diff failed (exit $LASTEXITCODE)." }
    $changed += @(& git -C $repoRoot ls-files -o --exclude-standard)
    if ($LASTEXITCODE) { throw "git ls-files failed (exit $LASTEXITCODE)." }
    @($changed | Where-Object { $_ -match $shippedPaths })
}

# True when buildNotes.txt differs from HEAD or is not tracked yet. git diff --quiet exits 1 for
# a difference; any other nonzero exit is a git failure, not a changed file.
function testBuildNotesChanged {
    $tracked = @(& git -C $repoRoot ls-files -- $buildNotes)
    if ($LASTEXITCODE) { throw "git ls-files failed (exit $LASTEXITCODE)." }
    if (-not $tracked) { return $true }
    # core.safecrlf=false only drops the "LF will be replaced by CRLF" warnings from read-only output.
    & git -C $repoRoot -c core.safecrlf=false diff --quiet HEAD -- $buildNotes
    switch ($LASTEXITCODE) {
        0 { return $false }
        1 { return $true }
        default { throw "git diff failed (exit $LASTEXITCODE)." }
    }
}

# buildNotes.md entries stamped after the last commit. The file is untracked and oldest-first, so
# match "## yyyy-MM-dd (~HH:mm)" headings anywhere; that stamp sorts as text.
function getNewBuildNotesEntries {
    $notesMd = "$repoRoot\buildNotes.md"
    if (-not (Test-Path -LiteralPath $notesMd)) { return @() }
    $head = (& git -C $repoRoot log -1 --format=%cd --date=format-local:'%Y-%m-%d (~%H:%M)' | Out-String).Trim()
    if ($LASTEXITCODE) { throw "git log failed (exit $LASTEXITCODE)." }
    $entries = [Collections.Generic.List[string]]::new()
    $inNew = $false
    foreach ($line in [IO.File]::ReadAllLines($notesMd)) {
        if ($line.StartsWith('## ')) {
            $inNew = $line -match '^## (\d{4}-\d{2}-\d{2} \(~\d{2}:\d{2}\))' -and [string]::CompareOrdinal($Matches[1], $head) -gt 0
        } elseif ($line -eq '---') {
            $inNew = $false
        }
        if ($inNew) { $entries.Add($line) }
    }
    @($entries)
}

# Throws unless $Subject is "<Prefix> [<label> > <summary>]", optionally "; <label> > <summary>".
# Summaries: no edge spaces, no trailing period, none of [ ] > ;. .NET regex is case-sensitive.
function assertCommitSubject {
    param([Parameter(Mandatory)][string]$Subject, [Parameter(Mandatory)][string]$Prefix)
    $labels = ($commitLabels | ForEach-Object { [regex]::Escape($_) }) -join '|'
    $segment = "($labels) > [^\s\[\]>;](?:[^\[\]>;]*[^\s.\[\]>;])?"
    $match = [regex]::Match($Subject, "^$([regex]::Escape($Prefix)) \[$segment(?:; $segment)?\]$")
    if (-not $match.Success) {
        throw "Line 1 must be '$Prefix [<label> > <summary>]', optionally '; <label> > <summary>'. " +
            "Labels: $($commitLabels -join ' ')"
    }
    $first, $second = $match.Groups[1].Value, $match.Groups[2].Value
    if ($second -and $first -eq $second) { throw "Line 1 repeats label '$first'; merge the segments." }
    if ($second -and 'misc.' -in $first, $second) { throw "'misc.' must be the only segment." }
    if ($Subject -match '[^\x20-\x7E]') { throw 'Line 1 must be plain ASCII.' }
    if ($Subject.Length -gt 72) { throw "Line 1 is $($Subject.Length) characters; the limit is 72." }
}

function getReleaseTagSegment {
    param([string]$Tag)
    $clean = ($Tag ?? '').Trim()
    if ([string]::IsNullOrWhiteSpace($clean)) { return 'untagged' }
    return $clean
}

function getMusicAppBuildPlatforms {
    param([string]$Architecture)
    if ($Architecture) { return @($Architecture) }
    @('x64', 'x86', 'arm64', 'portable')
}

function checkVerBuild {
    param([string]$Architecture)
    if (Test-Path -LiteralPath $versionBuild) { return }
    setVerBuild ($Architecture ?? 'x64')
}

function getArchitecture {
    param([string[]]$FlagArgs)
    $found = @()
    foreach ($arg in @($FlagArgs)) {
        if ([string]::IsNullOrWhiteSpace("$arg")) { continue }
        switch -Regex ("$arg".Trim()) {
            '(?i)^(x86|--x86|-x86|--86|-86)$' { $found += 'x86' }
            '(?i)^(x64|--x64|-x64|--64|-64)$' { $found += 'x64' }
            '(?i)^(arm64|--arm64|-arm64|--arm|-arm)$' { $found += 'arm64' }
            '(?i)^(--help|-h)$' { return 'help' }
            default { throw "Unknown architecture flag: $arg" }
        }
    }
    $unique = @($found | Select-Object -Unique)
    if ($unique.Count -gt 1) { throw "Conflicting architecture flags: $($unique -join ', ')" }
    if ($unique.Count -eq 1) { return $unique[0] }
    $null
}

function getBuildTargets {
    param([string]$Architecture)
    if (-not $Architecture) { return $buildTargets }
    $target = @($buildTargets | Where-Object Architecture -eq $Architecture)
    if (-not $target) { throw "No build target for architecture: $Architecture" }
    return ,$target[0]
}

function deleteDir {
    param([Parameter(Mandatory)][string]$Path)
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Recurse -Force }
}

function publishCleanup {
    param([Parameter(Mandatory)][string]$BinFolder)
    Get-ChildItem -LiteralPath $BinFolder -Filter '*.pdb' -File -ErrorAction SilentlyContinue | Remove-Item -Force
    foreach ($name in 'Avalonia.DesignerSupport.dll', 'Avalonia.Remote.Protocol.dll') {
        $path = "$BinFolder\$name"
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
}

function runNativeCommand {
    param([Parameter(Mandatory)][string]$FilePath, [Parameter(Mandatory)]$ArgumentList, [Parameter(Mandatory)][string]$Name)
    & $FilePath @ArgumentList
    if ($LASTEXITCODE) { throw "$Name failed (exit $LASTEXITCODE)." }
}

function writeClearedLine {
    param([Parameter(Mandatory)][string]$Text, [Parameter(Mandatory)][int]$PadWidth, [switch]$NoNewline)
    Write-Host "`r$Text$(' ' * [Math]::Max(0, $PadWidth - $Text.Length))" -NoNewline:$NoNewline
}

function closeOut {
    param([int]$Seconds = 5)
    if ($env:BASE_BUILD_PIPELINE) { return }
    $caller = $MyInvocation.PSCommandPath
    if ([string]::IsNullOrWhiteSpace($caller)) { return }
    $argv = [Environment]::GetCommandLineArgs()
    $fileArg = $null
    for ($i = 0; $i -lt $argv.Length; $i++) {
        if ("$($argv[$i])" -match '^(?i)-File$|^(?i)-f$') {
            if ($i + 1 -lt $argv.Length) { $fileArg = $argv[$i + 1] }
            break
        }
    }
    if ([string]::IsNullOrWhiteSpace($fileArg)) { return }
    try {
        $fileFull = [IO.Path]::GetFullPath($fileArg)
        $callerFull = [IO.Path]::GetFullPath($caller)
    } catch { return }
    if (-not [string]::Equals($fileFull, $callerFull, [StringComparison]::OrdinalIgnoreCase)) { return }
    if ($Seconds -lt 0) { $Seconds = 0 }
    if ($Seconds -gt 0) {
        $pad = "closing after $Seconds seconds..."
        foreach ($n in $Seconds..1) {
            writeClearedLine -Text "closing after $n seconds..." -PadWidth $pad.Length -NoNewline
            Start-Sleep -Seconds 1
        }
        writeClearedLine -Text 'closing...' -PadWidth $pad.Length
    }
    try {
        if ($env:SCRIPT_OWN_PANE -and $env:WEZTERM_PANE -and $weztermExe) { & $weztermExe @('cli', 'kill-pane', '--pane-id', $env:WEZTERM_PANE) }
    } catch { }
    [Environment]::Exit(0)
}

function openUrl {
    param([Parameter(Mandatory)][string]$Url)
    if ([string]::IsNullOrWhiteSpace($Url)) { throw 'openUrl requires a URL.' }
    Start-Process $Url
}

function convertToIco {
    param([Parameter(Mandatory)][string]$InputPath)
    $source = (Resolve-Path -LiteralPath $InputPath).Path
    if ([IO.Path]::GetExtension($source).ToLowerInvariant() -eq '.svg') { throw 'SVG input is not supported.' }
    $output = [IO.Path]::ChangeExtension($source, '.ico')
    $arguments = [Collections.Generic.List[string]]::new()
    $arguments.Add($source)
    foreach ($size in $imageSizes) {
        $arguments.AddRange(@('(', '-clone', '0', '-resize', "${size}x${size}", ')'))
    }
    $arguments.Add('-delete'); $arguments.Add('0'); $arguments.Add($output)
    runNativeCommand -FilePath 'magick' -ArgumentList $arguments.ToArray() -Name 'ImageMagick ICO conversion'
    $output
}

function resizePng {
    param(
        [Parameter(Mandatory)][string]$InputPath,
        [int[]]$Sizes = $imageSizes
    )
    $source = (Resolve-Path -LiteralPath $InputPath).Path
    if ([IO.Path]::GetExtension($source).ToLowerInvariant() -eq '.svg') { throw 'SVG input is not supported.' }
    $directory = [IO.Path]::GetDirectoryName($source)
    $baseName = [IO.Path]::GetFileNameWithoutExtension($source)
    foreach ($size in $Sizes) {
        if ($size -le 0) { throw "Image size must be positive: $size" }
        $output = "$directory\${baseName}${size}.png"
        runNativeCommand -FilePath 'magick' -ArgumentList @($source, '-resize', "${size}x${size}", $output) -Name "ImageMagick PNG resize ($size)"
        $output
    }
}

function writeWizardImages {
    param([string]$InputPath = $iconPng)
    $source = (Resolve-Path -LiteralPath $InputPath).Path
    if ([IO.Path]::GetExtension($source).ToLowerInvariant() -eq '.svg') { throw 'SVG input is not supported.' }
    runNativeCommand -FilePath 'magick' -ArgumentList @($source, '-resize', '256x256', $wizardSmallPng) -Name 'ImageMagick wizard small resize'
    runNativeCommand -FilePath 'magick' -ArgumentList @($source, '-background', 'black', '-gravity', 'center', '-resize', '240x459', '-extent', '240x459', $wizardLargePng) -Name 'ImageMagick wizard large resize'
}

function prepareInstallerImages {
    param([string]$InputPath = $iconPng)
    resizePng -InputPath $InputPath -Sizes $pngInstallerSizes
    writeWizardImages -InputPath $InputPath
}

function ensureInstallerImages {
    if (-not (Test-Path -LiteralPath $iconPng)) { throw "Missing source icon: $iconPng" }
    if (-not (Test-Path -LiteralPath $iconIco)) { convertToIco -InputPath $iconPng | Out-Null }
    if (-not (Test-Path -LiteralPath $wizardSmallPng) -or -not (Test-Path -LiteralPath $wizardLargePng)) {
        writeWizardImages -InputPath $iconPng
    }
}

function buildAssetName {
    param([Parameter(Mandatory)][ValidateSet('Installer', 'Portable')][string]$Kind, [Parameter(Mandatory)][string]$Architecture)
    $segment = getReleaseTagSegment -Tag $versionTagContents
    if ($Kind -eq 'Installer') { return "musicApp-v${versionContents}-${segment}-$Architecture-installer.exe" }
    "musicApp-v${versionContents}-${segment}-portable.zip"
}

function Normalize-MarkdownLineArray {
    param([AllowNull()] $Lines)
    if ($null -eq $Lines) { return @() }
    if ($Lines -is [string]) {
        if ([string]::IsNullOrEmpty($Lines)) { return @() }
        return @($Lines -split '\r?\n')
    }
    [string[]]@($Lines | ForEach-Object { "$_" })
}

function Trim-BlankLinesAtEdge {
    param([AllowNull()] $Lines, [Parameter(Mandatory)][bool]$TrimEnd)
    $Lines = @(Normalize-MarkdownLineArray -Lines $Lines)
    if ($Lines.Count -eq 0) { return @() }
    if ($TrimEnd) {
        $end = $Lines.Count - 1
        while ($end -ge 0 -and ($Lines[$end] -match '^\s*$')) { $end-- }
        if ($end -lt 0) { return @() }
        return @($Lines[0..$end])
    }
    $start = 0
    while ($start -lt $Lines.Count -and ($Lines[$start] -match '^\s*$')) { $start++ }
    if ($start -ge $Lines.Count) { return @() }
    @($Lines[$start..($Lines.Count - 1)])
}

function Trim-TrailingBlankLines { param([AllowNull()] $Lines) Trim-BlankLinesAtEdge -Lines $Lines -TrimEnd $true }
function Trim-LeadingBlankLines { param([AllowNull()] $Lines) Trim-BlankLinesAtEdge -Lines $Lines -TrimEnd $false }

function Build-HeaderTree {
    param([string[]]$Lines, [int]$RootLevel = 0)
    $headerRegex = '^(?<hash>#{2,5})\s+(?<title>.+?)\s*$'
    $rootNode = [pscustomobject]@{ Level = $RootLevel; Title = 'ROOT'; HeaderIndex = -1; Parent = $null; Children = [System.Collections.Generic.List[object]]::new(); DirectContentStartIndex = 0; DirectContentEndIndex = -1; UpdatedDirectContentLines = @() }
    $stack = [System.Collections.Generic.Stack[object]]::new(); [void]$stack.Push($rootNode)
    $nodes = [System.Collections.Generic.List[object]]::new()
    for ($i = 0; $i -lt $Lines.Length; $i++) {
        $m = [regex]::Match($Lines[$i], $headerRegex); if (-not $m.Success) { continue }
        $level = $m.Groups['hash'].Value.Length; $title = $m.Groups['title'].Value
        while ($stack.Count -gt 1 -and ($stack.Peek()).Level -ge $level) { [void]$stack.Pop() }
        $parent = $stack.Peek()
        $node = [pscustomobject]@{ Level = $level; Title = $title; HeaderIndex = $i; Parent = $parent; Children = [System.Collections.Generic.List[object]]::new(); DirectContentStartIndex = $i + 1; DirectContentEndIndex = -1; UpdatedDirectContentLines = @() }
        [void]$parent.Children.Add($node); [void]$nodes.Add($node); [void]$stack.Push($node)
    }
    $nodesByIndex = $nodes.ToArray()
    $firstHeaderIndex = if ($nodesByIndex.Length -gt 0) { $nodesByIndex[0].HeaderIndex } else { $Lines.Length }
    $rootNode.DirectContentEndIndex = $firstHeaderIndex - 1
    for ($ni = 0; $ni -lt $nodesByIndex.Length; $ni++) {
        $node = $nodesByIndex[$ni]
        if ($node.Children.Count -gt 0) { $node.DirectContentEndIndex = $node.Children[0].HeaderIndex - 1; continue }
        $next = $null
        for ($j = $ni + 1; $j -lt $nodesByIndex.Length; $j++) {
            if ($nodesByIndex[$j].Level -le $node.Level) { $next = $nodesByIndex[$j]; break }
        }
        $node.DirectContentEndIndex = if ($null -ne $next) { $next.HeaderIndex - 1 } else { $Lines.Length - 1 }
    }
    [pscustomobject]@{ Root = $rootNode; Nodes = $nodesByIndex }
}

function Get-NodePathKey {
    param([object]$Node)
    $titles = [System.Collections.Generic.List[string]]::new()
    $cur = $Node
    while ($null -ne $cur -and $cur.Level -gt 0) { [void]$titles.Add($cur.Title); $cur = $cur.Parent }
    $titles.Reverse(); $titles -join '|||'
}

function Get-TasksBullets {
    param([string[]]$RegionLines)
    $re = '^(?<indent>\s*)(?:-\s*)?~~(?<text>.*?)~~\s*$'
    $bullets = [System.Collections.Generic.List[object]]::new()
    foreach ($line in $RegionLines) {
        $m = [regex]::Match($line, $re); if (-not $m.Success) { continue }
        [void]$bullets.Add([pscustomobject]@{ Prefix = $m.Groups['indent'].Value + '- '; Text = $m.Groups['text'].Value })
    }
    $bullets.ToArray()
}

function Update-FeatureDirectContent {
    param([string[]]$FeatureRegionLines, [object[]]$TasksBullets)
    $bulletIndices = [System.Collections.Generic.List[int]]::new()
    for ($i = 0; $i -lt $FeatureRegionLines.Length; $i++) { if ($FeatureRegionLines[$i] -match '^\s*-\s*') { [void]$bulletIndices.Add($i) } }
    $insertLines = @($TasksBullets | ForEach-Object { $_.Prefix + $_.Text })
    if ($bulletIndices.Count -gt 0) {
        $firstBulletIdx = $bulletIndices[0]; $lastBulletIdx = $bulletIndices[$bulletIndices.Count - 1]
        $before = if ($firstBulletIdx -gt 0) { $FeatureRegionLines[0..($firstBulletIdx - 1)] } else { @() }
        $afterStart = $lastBulletIdx + 1
        $after = if ($afterStart -lt $FeatureRegionLines.Length) { $FeatureRegionLines[$afterStart..($FeatureRegionLines.Length - 1)] } else { @() }
        return @($before + $insertLines + $after)
    }
    $lastNonBlankIdx = -1
    for ($i = $FeatureRegionLines.Length - 1; $i -ge 0; $i--) { if ($FeatureRegionLines[$i] -notmatch '^\s*$') { $lastNonBlankIdx = $i; break } }
    $prefixLines = if ($lastNonBlankIdx -ge 0) { $FeatureRegionLines[0..$lastNonBlankIdx] } else { @() }
    $trailingLines = if ($lastNonBlankIdx -lt $FeatureRegionLines.Length - 1) { $FeatureRegionLines[($lastNonBlankIdx + 1)..($FeatureRegionLines.Length - 1)] } else { @() }
    @($prefixLines + $insertLines + $trailingLines)
}

function Get-LineSlice {
    param([string[]]$Lines, [int]$Start, [int]$End)
    if ($Start -le $End) { $Lines[$Start..$End] } else { @() }
}

function Get-FeatureTreeNodeLines {
    param([object]$Node, [string[]]$SourceFeatureLines)
    $out = [System.Collections.Generic.List[string]]::new()
    if ($Node.Level -gt 0) { [void]$out.Add($SourceFeatureLines[$Node.HeaderIndex]) }
    $upd = [string[]]$Node.UpdatedDirectContentLines; if ($upd.Length -gt 0) { [void]$out.AddRange($upd) }
    foreach ($child in $Node.Children) { [void]$out.AddRange([string[]](Get-FeatureTreeNodeLines -Node $child -SourceFeatureLines $SourceFeatureLines)) }
    $out.ToArray()
}

function Get-MarkdownTopLevelSectionLines {
    param([AllowNull()] $Lines, [Parameter(Mandatory)][string]$SectionTitle)
    $Lines = @(Normalize-MarkdownLineArray -Lines $Lines); if ($Lines.Count -eq 0) { return @() }
    $startPattern = '^##\s+' + [regex]::Escape($SectionTitle) + '\s*$'
    $hit = $Lines | Select-String -Pattern $startPattern | Select-Object -First 1; if (-not $hit) { return @() }
    $startIdx = $hit.LineNumber - 1; $endIdx = $Lines.Length - 1
    for ($i = $startIdx + 1; $i -lt $Lines.Length; $i++) { if ($Lines[$i] -match '^##\s+') { $endIdx = $i - 1; break } }
    if ($endIdx -lt $startIdx) { return @() }; Trim-TrailingBlankLines @($Lines[$startIdx..$endIdx])
}

function Merge-FeaturesFromTasks {
    param([AllowNull()] $TaskLines, [AllowNull()] $FeatureLines)
    [string[]]$TaskLines = @(Normalize-MarkdownLineArray -Lines $TaskLines)
    [string[]]$FeatureLines = @(Normalize-MarkdownLineArray -Lines $FeatureLines)
    $taskTree = Build-HeaderTree -Lines $TaskLines; $featureTree = Build-HeaderTree -Lines $FeatureLines
    $taskLookup = @{}; foreach ($tNode in $taskTree.Nodes) { $taskLookup[(Get-NodePathKey -Node $tNode)] = $tNode }
    foreach ($fNode in $featureTree.Nodes) {
        $featureRegion = Get-LineSlice -Lines $FeatureLines -Start $fNode.DirectContentStartIndex -End $fNode.DirectContentEndIndex
        $fNode.UpdatedDirectContentLines = @($featureRegion)
        $nodeKey = Get-NodePathKey -Node $fNode; if (-not $taskLookup.ContainsKey($nodeKey)) { continue }
        $tNode = $taskLookup[$nodeKey]
        $taskRegion = Get-LineSlice -Lines $TaskLines -Start $tNode.DirectContentStartIndex -End $tNode.DirectContentEndIndex
        $tasksBullets = @(Get-TasksBullets -RegionLines $taskRegion)
        if ($tasksBullets.Length -gt 0) { $fNode.UpdatedDirectContentLines = @(Update-FeatureDirectContent -FeatureRegionLines @($featureRegion) -TasksBullets $tasksBullets) }
    }
    $featureTree.Root.UpdatedDirectContentLines = @(Get-LineSlice -Lines $FeatureLines -Start $featureTree.Root.DirectContentStartIndex -End $featureTree.Root.DirectContentEndIndex)
    $rendered = [System.Collections.Generic.List[string]]::new()
    foreach ($top in $featureTree.Root.Children) { [void]$rendered.AddRange([string[]](Get-FeatureTreeNodeLines -Node $top -SourceFeatureLines $FeatureLines)) }
    $rendered.ToArray()
}

function buildAll {
    param([string]$Architecture)
    $stepArgs = @()
    if ($Architecture) { $stepArgs = @('-Architecture', $Architecture) }
    $previousPipeline = $env:BASE_BUILD_PIPELINE
    try {
        $env:BASE_BUILD_PIPELINE = '1'
        runNativeCommand -FilePath "$PSScriptRoot\build.ps1" -ArgumentList $stepArgs -Name 'build.ps1'
        runNativeCommand -FilePath "$PSScriptRoot\buildInstaller.ps1" -ArgumentList $stepArgs -Name 'buildInstaller.ps1'
        runNativeCommand -FilePath "$PSScriptRoot\taskCounter.ps1" -ArgumentList @() -Name 'taskCounter.ps1'
        runNativeCommand -FilePath "$PSScriptRoot\writeFeatures.ps1" -ArgumentList @() -Name 'writeFeatures.ps1'
        runNativeCommand -FilePath "$PSScriptRoot\updateReadme.ps1" -ArgumentList @() -Name 'updateReadme.ps1'
        runNativeCommand -FilePath "$PSScriptRoot\countCode.ps1" -ArgumentList @() -Name 'countCode.ps1'
        runNativeCommand -FilePath "$PSScriptRoot\syncWeb.ps1" -ArgumentList @() -Name 'syncWeb.ps1'
    } finally {
        $env:BASE_BUILD_PIPELINE = $previousPipeline
    }
}

$versionLines = readVerFile
$versionContents = $versionLines[0].Trim()
$versionTagContents = $versionLines[1].Trim()
$versionBuildContents = $versionLines[2].Trim()
$releaseTag = "v$versionContents"
$tasksRaw = Get-Content -LiteralPath $tasks -Raw -Encoding UTF8
$featuresRaw = Get-Content -LiteralPath $features -Raw -Encoding UTF8

Set-Location -LiteralPath $repoRoot
