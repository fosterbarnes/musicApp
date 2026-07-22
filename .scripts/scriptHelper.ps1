#. "$PSScriptRoot\scriptHelper.ps1"
$root = Split-Path -Path $PSScriptRoot -Parent
$appRoot = "$root\musicApp"
$version = "$appRoot\Version"
$buildNotes = "$root\buildNotes.txt"

function Read-VersionFileLines {
    param([string]$LiteralPath = $version)
    $raw = [System.IO.File]::ReadAllText($LiteralPath)
    $lines = @($raw -split '\r?\n' | ForEach-Object { $_.TrimEnd() })
    while ($lines.Count -lt 3) { $lines += '' }
    return $lines
}

$versionLines = Read-VersionFileLines
$versionContents = $versionLines[0].Trim()
$versionTagContents = $versionLines[1].Trim()
$versionBuildContents = $versionLines[2].Trim()
$buildNotesContents = if (Test-Path -LiteralPath $buildNotes) {
    [System.IO.File]::ReadAllText($buildNotes).Trim()
} else { "" }
$updater = "$appRoot\.updater"
$updaterSwap = "$appRoot\.updater.swap"
$scripts = "$root\.scripts"
$readme = "$root\README.md"
$tasks = "$root\.md\Tasks.md"
$features = "$root\.md\Features.md"
$tasksRaw = Get-Content -LiteralPath $tasks -Raw -Encoding UTF8
$featuresRaw = Get-Content -LiteralPath $features -Raw -Encoding UTF8
$musicAppCsproj = "$appRoot\musicApp.csproj"

$Utf8NoBomEncoding = New-Object System.Text.UTF8Encoding $false

function Write-RepoUtf8NoBomFile {
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [Parameter(Mandatory)][string]$Content
    )
    [System.IO.File]::WriteAllText($LiteralPath, $Content, $Utf8NoBomEncoding)
}

function Write-VersionFile {
    param(
        [Parameter(Mandatory)][string]$SemVer,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Tag,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Build,
        [string]$LiteralPath = $version
    )
    $content = (($SemVer.Trim(), $Tag.Trim(), $Build.Trim()) -join [Environment]::NewLine) + [Environment]::NewLine
    Write-RepoUtf8NoBomFile -LiteralPath $LiteralPath -Content $content
}

function Set-VersionBuildPlatform {
    param(
        [Parameter(Mandatory)][string]$Platform,
        [string]$LiteralPath = $version
    )
    $lines = Read-VersionFileLines -LiteralPath $LiteralPath
    Write-VersionFile -SemVer $lines[0] -Tag $lines[1] -Build $Platform -LiteralPath $LiteralPath
    $script:versionBuildContents = $Platform.Trim()
}

function Get-ReleaseTagSegment {
    param([string]$Tag)
    $clean = ($Tag ?? "").Trim()
    if ([string]::IsNullOrWhiteSpace($clean)) { return "untagged" }
    return $clean
}

function Get-RepoRootFromMusicAppScripts {
    param([string]$MusicAppScriptsDir = $PSScriptRoot)
    Split-Path (Split-Path $MusicAppScriptsDir -Parent) -Parent
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
    param(
        [AllowNull()] $Lines,
        [Parameter(Mandatory)][bool]$TrimEnd
    )
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
    param(
        [string[]]$Lines,
        [int]$RootLevel = 0
    )

    $headerRegex = '^(?<hash>#{2,5})\s+(?<title>.+?)\s*$'
    $root = [pscustomobject]@{
        Level                    = $RootLevel
        Title                    = 'ROOT'
        HeaderIndex              = -1
        Parent                   = $null
        Children                 = [System.Collections.Generic.List[object]]::new()
        DirectContentStartIndex  = 0
        DirectContentEndIndex    = -1
        UpdatedDirectContentLines = @()
    }

    $stack = [System.Collections.Generic.Stack[object]]::new()
    [void]$stack.Push($root)

    $nodes = [System.Collections.Generic.List[object]]::new()

    for ($i = 0; $i -lt $Lines.Length; $i++) {
        $m = [regex]::Match($Lines[$i], $headerRegex)
        if (-not $m.Success) { continue }

        $level = $m.Groups['hash'].Value.Length
        $title = $m.Groups['title'].Value

        while ($stack.Count -gt 1 -and ($stack.Peek()).Level -ge $level) {
            [void]$stack.Pop()
        }

        $parent = $stack.Peek()
        $node = [pscustomobject]@{
            Level                    = $level
            Title                    = $title
            HeaderIndex              = $i
            Parent                   = $parent
            Children                 = [System.Collections.Generic.List[object]]::new()
            DirectContentStartIndex  = $i + 1
            DirectContentEndIndex    = -1
            UpdatedDirectContentLines = @()
        }

        [void]$parent.Children.Add($node)
        [void]$nodes.Add($node)
        [void]$stack.Push($node)
    }

    $nodesByIndex = $nodes.ToArray()
    $firstHeaderIndex = if ($nodesByIndex.Length -gt 0) { $nodesByIndex[0].HeaderIndex } else { $Lines.Length }
    $root.DirectContentEndIndex = $firstHeaderIndex - 1

    for ($ni = 0; $ni -lt $nodesByIndex.Length; $ni++) {
        $node = $nodesByIndex[$ni]
        if ($node.Children.Count -gt 0) {
            $node.DirectContentEndIndex = $node.Children[0].HeaderIndex - 1
            continue
        }

        $next = $null
        for ($j = $ni + 1; $j -lt $nodesByIndex.Length; $j++) {
            if ($nodesByIndex[$j].Level -le $node.Level) {
                $next = $nodesByIndex[$j]
                break
            }
        }

        $node.DirectContentEndIndex = if ($null -ne $next) { $next.HeaderIndex - 1 } else { $Lines.Length - 1 }
    }

    [pscustomobject]@{
        Root  = $root
        Nodes = $nodesByIndex
    }
}

function Get-NodePathKey {
    param([object]$Node)

    $titles = [System.Collections.Generic.List[string]]::new()
    $cur = $Node
    while ($null -ne $cur -and $cur.Level -gt 0) {
        [void]$titles.Add($cur.Title)
        $cur = $cur.Parent
    }

    $titles.Reverse()
    $titles -join '|||'
}

function Get-TasksBullets {
    param([string[]]$RegionLines)

    $re = '^(?<indent>\s*)(?:-\s*)?~~(?<text>.*?)~~\s*$'
    $bullets = [System.Collections.Generic.List[object]]::new()

    foreach ($line in $RegionLines) {
        $m = [regex]::Match($line, $re)
        if (-not $m.Success) { continue }
        [void]$bullets.Add([pscustomobject]@{
            Prefix = $m.Groups['indent'].Value + '- '
            Text   = $m.Groups['text'].Value
        })
    }

    $bullets.ToArray()
}

function Update-FeatureDirectContent {
    param(
        [string[]]$FeatureRegionLines,
        [object[]]$TasksBullets
    )

    $bulletIndices = [System.Collections.Generic.List[int]]::new()
    for ($i = 0; $i -lt $FeatureRegionLines.Length; $i++) {
        if ($FeatureRegionLines[$i] -match '^\s*-\s*') {
            [void]$bulletIndices.Add($i)
        }
    }

    $insertLines = @($TasksBullets | ForEach-Object { $_.Prefix + $_.Text })

    if ($bulletIndices.Count -gt 0) {
        $firstBulletIdx = $bulletIndices[0]
        $lastBulletIdx = $bulletIndices[$bulletIndices.Count - 1]

        $before = if ($firstBulletIdx -gt 0) {
            $FeatureRegionLines[0..($firstBulletIdx - 1)]
        }
        else { @() }

        $afterStart = $lastBulletIdx + 1
        $after = if ($afterStart -lt $FeatureRegionLines.Length) {
            $FeatureRegionLines[$afterStart..($FeatureRegionLines.Length - 1)]
        }
        else { @() }

        return @($before + $insertLines + $after)
    }

    $lastNonBlankIdx = -1
    for ($i = $FeatureRegionLines.Length - 1; $i -ge 0; $i--) {
        if ($FeatureRegionLines[$i] -notmatch '^\s*$') {
            $lastNonBlankIdx = $i
            break
        }
    }

    $prefixLines = if ($lastNonBlankIdx -ge 0) {
        $FeatureRegionLines[0..$lastNonBlankIdx]
    }
    else { @() }

    $trailingLines = if ($lastNonBlankIdx -lt $FeatureRegionLines.Length - 1) {
        $FeatureRegionLines[($lastNonBlankIdx + 1)..($FeatureRegionLines.Length - 1)]
    }
    else { @() }

    @($prefixLines + $insertLines + $trailingLines)
}

function Get-LineSlice {
    param(
        [string[]]$Lines,
        [int]$Start,
        [int]$End
    )
    if ($Start -le $End) { $Lines[$Start..$End] } else { @() }
}

function Get-FeatureTreeNodeLines {
    param(
        [object]$Node,
        [string[]]$SourceFeatureLines
    )

    $out = [System.Collections.Generic.List[string]]::new()

    if ($Node.Level -gt 0) { [void]$out.Add($SourceFeatureLines[$Node.HeaderIndex]) }

    $upd = [string[]]$Node.UpdatedDirectContentLines
    if ($upd.Length -gt 0) { [void]$out.AddRange($upd) }

    foreach ($child in $Node.Children) {
        [void]$out.AddRange([string[]](Get-FeatureTreeNodeLines -Node $child -SourceFeatureLines $SourceFeatureLines))
    }

    $out.ToArray()
}

function Get-MarkdownTopLevelSectionLines {
    param(
        [AllowNull()]
        $Lines,
        [Parameter(Mandatory)]
        [string]$SectionTitle
    )

    $Lines = @(Normalize-MarkdownLineArray -Lines $Lines)
    if ($Lines.Count -eq 0) { return @() }

    $startPattern = '^##\s+' + [regex]::Escape($SectionTitle) + '\s*$'
    $hit = $Lines | Select-String -Pattern $startPattern | Select-Object -First 1
    if (-not $hit) { return @() }
    $startIdx = $hit.LineNumber - 1

    $endIdx = $Lines.Length - 1
    for ($i = $startIdx + 1; $i -lt $Lines.Length; $i++) {
        if ($Lines[$i] -match '^##\s+') {
            $endIdx = $i - 1
            break
        }
    }

    if ($endIdx -lt $startIdx) { return @() }
    $section = @($Lines[$startIdx..$endIdx])
    return (Trim-TrailingBlankLines $section)
}

function Merge-FeaturesFromTasks {
    param(
        [AllowNull()] $TaskLines,
        [AllowNull()] $FeatureLines
    )

    [string[]]$TaskLines = @(Normalize-MarkdownLineArray -Lines $TaskLines)
    [string[]]$FeatureLines = @(Normalize-MarkdownLineArray -Lines $FeatureLines)

    $taskTree = Build-HeaderTree -Lines $TaskLines
    $featureTree = Build-HeaderTree -Lines $FeatureLines

    $taskLookup = @{}
    foreach ($tNode in $taskTree.Nodes) {
        $taskLookup[(Get-NodePathKey -Node $tNode)] = $tNode
    }

    foreach ($fNode in $featureTree.Nodes) {
        $featureRegion = Get-LineSlice -Lines $FeatureLines -Start $fNode.DirectContentStartIndex -End $fNode.DirectContentEndIndex

        $fNode.UpdatedDirectContentLines = @($featureRegion)
        $nodeKey = Get-NodePathKey -Node $fNode
        if (-not $taskLookup.ContainsKey($nodeKey)) { continue }

        $tNode = $taskLookup[$nodeKey]
        $taskRegion = Get-LineSlice -Lines $TaskLines -Start $tNode.DirectContentStartIndex -End $tNode.DirectContentEndIndex

        $tasksBullets = @(Get-TasksBullets -RegionLines $taskRegion)
        if ($tasksBullets.Length -gt 0) {
            $fNode.UpdatedDirectContentLines = @(
                Update-FeatureDirectContent -FeatureRegionLines @($featureRegion) -TasksBullets $tasksBullets
            )
        }
    }

    $featureTree.Root.UpdatedDirectContentLines = @(
        Get-LineSlice -Lines $FeatureLines -Start $featureTree.Root.DirectContentStartIndex -End $featureTree.Root.DirectContentEndIndex
    )

    $rendered = [System.Collections.Generic.List[string]]::new()
    foreach ($top in $featureTree.Root.Children) {
        [void]$rendered.AddRange([string[]](Get-FeatureTreeNodeLines -Node $top -SourceFeatureLines $FeatureLines))
    }
    $rendered.ToArray()
}
