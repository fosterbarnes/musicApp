Set-StrictMode -Version Latest

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
    $null = $stack.Push($root)

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

    $nodesByIndex = @($nodes | Sort-Object HeaderIndex)
    $firstHeaderIndex = if ($nodesByIndex.Length -gt 0) { $nodesByIndex[0].HeaderIndex } else { $Lines.Length }
    $root.DirectContentEndIndex = $firstHeaderIndex - 1

    foreach ($node in $nodesByIndex) {
        if ($node.Children.Count -gt 0) {
            $node.DirectContentEndIndex = $node.Children[0].HeaderIndex - 1
            continue
        }

        $next = $nodesByIndex |
            Where-Object { $_.HeaderIndex -gt $node.HeaderIndex -and $_.Level -le $node.Level } |
            Sort-Object HeaderIndex |
            Select-Object -First 1

        $node.DirectContentEndIndex = if ($null -ne $next) {
            $next.HeaderIndex - 1
        }
        else {
            $Lines.Length - 1
        }
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

    $bullets = [System.Collections.Generic.List[object]]::new()

    foreach ($line in $RegionLines) {
        $m1 = [regex]::Match($line, '^(?<indent>\s*)-\s*~~(?<text>.*?)~~\s*$')
        if ($m1.Success) {
            $null = $bullets.Add([pscustomobject]@{
                Prefix = $m1.Groups['indent'].Value + '- '
                Text   = $m1.Groups['text'].Value
            })
            continue
        }

        $m2 = [regex]::Match($line, '^(?<indent>\s*)~~(?<text>.*?)~~\s*$')
        if ($m2.Success) {
            $null = $bullets.Add([pscustomobject]@{
                Prefix = $m2.Groups['indent'].Value + '- '
                Text   = $m2.Groups['text'].Value
            })
        }
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

    $insertLines = foreach ($b in @($TasksBullets)) { $b.Prefix + $b.Text }

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
    for ($i = 0; $i -lt $FeatureRegionLines.Length; $i++) {
        if ($FeatureRegionLines[$i] -notmatch '^\s*$') {
            $lastNonBlankIdx = $i
        }
    }

    $prefixLines = if ($lastNonBlankIdx -ge 0) {
        $FeatureRegionLines[0..$lastNonBlankIdx]
    }
    else { @() }

    $trailingLines = if ($lastNonBlankIdx + 1 -le $FeatureRegionLines.Length - 1) {
        $FeatureRegionLines[($lastNonBlankIdx + 1)..($FeatureRegionLines.Length - 1)]
    }
    else { @() }

    @($prefixLines + $insertLines + $trailingLines)
}

function Get-FeatureTreeNodeLines {
    param(
        [object]$Node,
        [string[]]$SourceFeatureLines
    )

    $out = [System.Collections.Generic.List[string]]::new()

    if ($Node.Level -gt 0) {
        [void]$out.Add($SourceFeatureLines[$Node.HeaderIndex])
    }

    foreach ($line in $Node.UpdatedDirectContentLines) {
        [void]$out.Add($line)
    }

    foreach ($child in $Node.Children) {
        foreach ($childLine in (Get-FeatureTreeNodeLines -Node $child -SourceFeatureLines $SourceFeatureLines)) {
            [void]$out.Add($childLine)
        }
    }

    $out.ToArray()
}

function Merge-FeaturesFromTasks {
    param(
        [string[]]$TaskLines,
        [string[]]$FeatureLines
    )

    $taskTree = Build-HeaderTree -Lines $TaskLines
    $featureTree = Build-HeaderTree -Lines $FeatureLines

    $taskLookup = @{}
    foreach ($tNode in $taskTree.Nodes) {
        $taskLookup[(Get-NodePathKey -Node $tNode)] = $tNode
    }

    foreach ($fNode in $featureTree.Nodes) {
        $regionStart = $fNode.DirectContentStartIndex
        $regionEnd = $fNode.DirectContentEndIndex
        $featureRegion = if ($regionStart -le $regionEnd) {
            $FeatureLines[$regionStart..$regionEnd]
        }
        else { @() }

        $fNode.UpdatedDirectContentLines = @($featureRegion)
        $nodeKey = Get-NodePathKey -Node $fNode
        if (-not $taskLookup.ContainsKey($nodeKey)) { continue }

        $tNode = $taskLookup[$nodeKey]
        $taskRegion = if ($tNode.DirectContentStartIndex -le $tNode.DirectContentEndIndex) {
            $TaskLines[$tNode.DirectContentStartIndex..$tNode.DirectContentEndIndex]
        }
        else { @() }

        $tasksBullets = @(Get-TasksBullets -RegionLines $taskRegion)
        if ($tasksBullets.Length -gt 0) {
            $fNode.UpdatedDirectContentLines = @(
                Update-FeatureDirectContent -FeatureRegionLines @($featureRegion) -TasksBullets $tasksBullets
            )
        }
    }

    # Keep current behavior exactly: root preface content is still ignored when rendering.
    $featureTree.Root.UpdatedDirectContentLines = @(
        if ($featureTree.Root.DirectContentStartIndex -le $featureTree.Root.DirectContentEndIndex) {
            $FeatureLines[$featureTree.Root.DirectContentStartIndex..$featureTree.Root.DirectContentEndIndex]
        }
        else { @() }
    )

    $rendered = [System.Collections.Generic.List[string]]::new()
    foreach ($top in $featureTree.Root.Children) {
        foreach ($line in (Get-FeatureTreeNodeLines -Node $top -SourceFeatureLines $FeatureLines)) {
            [void]$rendered.Add($line)
        }
    }

    $rendered.ToArray()
}

function Sync-ReadmeGeneralUsageFromFeatures {
    param(
        [string[]]$ReadmeLines,
        [string[]]$FeaturesUpdatedLines
    )

    $markerIdx = -1
    for ($i = 0; $i -lt $ReadmeLines.Length; $i++) {
        if ($ReadmeLines[$i] -match '^\#\s+General Usage Info\s*$') {
            $markerIdx = $i
            break
        }
    }
    if ($markerIdx -lt 0) {
        throw "Could not find '# General Usage Info' in README.md"
    }

    $featureTopHeadings = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($line in $FeaturesUpdatedLines) {
        $m = [regex]::Match($line, '^##\s+(.+?)\s*$')
        if ($m.Success) {
            [void]$featureTopHeadings.Add($m.Groups[1].Value.Trim())
        }
    }

    $startIdx = -1
    for ($i = $markerIdx + 1; $i -lt $ReadmeLines.Length; $i++) {
        $m = [regex]::Match($ReadmeLines[$i], '^##\s+(.+?)\s*$')
        if (-not $m.Success) { continue }

        $title = $m.Groups[1].Value.Trim()
        if ($featureTopHeadings.Contains($title)) {
            $startIdx = $i
            break
        }
    }

    if ($startIdx -lt 0) {
        $startIdx = $markerIdx + 1
    }

    $endIdx = $ReadmeLines.Length - 1
    for ($i = $startIdx + 1; $i -lt $ReadmeLines.Length; $i++) {
        $m = [regex]::Match($ReadmeLines[$i], '^##\s+(.+?)\s*$')
        if (-not $m.Success) { continue }

        $title = $m.Groups[1].Value.Trim()
        if (-not $featureTopHeadings.Contains($title)) {
            $endIdx = $i - 1
            break
        }
    }

    $trimmedFeatures = @($FeaturesUpdatedLines)
    while ($trimmedFeatures.Length -gt 0 -and ($trimmedFeatures[$trimmedFeatures.Length - 1] -match '^\s*$')) {
        if ($trimmedFeatures.Length -eq 1) {
            $trimmedFeatures = @()
            break
        }
        $trimmedFeatures = $trimmedFeatures[0..($trimmedFeatures.Length - 2)]
    }

    $prefix = if ($startIdx -gt 0) {
        $ReadmeLines[0..($startIdx - 1)]
    }
    else { @() }

    $suffixStart = $endIdx + 1
    $suffix = if ($suffixStart -lt $ReadmeLines.Length) {
        $ReadmeLines[$suffixStart..($ReadmeLines.Length - 1)]
    }
    else { @() }

    @($prefix + @('') + @($trimmedFeatures) + @('') + $suffix)
}
