# Version helper. Parse $args so -+, -++, -+++ work unquoted in pwsh.
. "$PSScriptRoot\scriptHelper.ps1"

function Show-NewVersionHelp {
    Write-Host @"
  .\newVersion.ps1 -h               Show help.
  .\newVersion.ps1 -tag             Generate a new Version tag (line 2)
  .\newVersion.ps1 -+               Major bump: 1.0.0 -> 2.0.0
  .\newVersion.ps1 -++              Minor bump: 0.1.0 -> 0.2.0
  .\newVersion.ps1 -+++             Patch bump: 0.0.1 -> 0.0.2
  .\newVersion.ps1 -tag -+++        Combine -tag with one bump (-+, -++, or -+++)
"@
}

function Update-BuildNotesHeader {
    $lines = Read-VersionFileLines
    $firstLine = "v$($lines[0].Trim()) release ($($lines[1].Trim()))"

    $existingTail = @()
    if (Test-Path -LiteralPath $buildNotes) {
        $prev = [System.IO.File]::ReadAllLines($buildNotes)
        if ($prev.Length -gt 1) { $existingTail = $prev[1..($prev.Length - 1)] }
    }
    $out = ((@($firstLine) + @($existingTail)) -join [Environment]::NewLine) + [Environment]::NewLine
    Write-RepoUtf8NoBomFile -LiteralPath $buildNotes -Content $out
}

function Read-SemVerParts {
    $lines = Read-VersionFileLines
    $raw = $lines[0].Trim()
    $p = @($raw -split '\.')
    if ($p.Count -ne 3) { throw "Version must be major.minor.patch (got '$raw')." }
    foreach ($x in $p) { if ($x -notmatch '^\d+$') { throw "Invalid version: $raw" } }
    return @{
        Major = [int]$p[0]
        Minor = [int]$p[1]
        Patch = [int]$p[2]
        Tag   = $lines[1]
        Build = $lines[2]
    }
}

$flags = @(
    $args |
        ForEach-Object { "$_".Trim() } |
        Where-Object { $_.Length -gt 0 }
)

if ($flags.Count -eq 0) { Show-NewVersionHelp; exit 0 }

$wantHelp = $false
$wantTag = $false
$bump = $null
foreach ($f in $flags) {
    $next = switch -Regex ($f) {
        '^(?i)(-h|--h|-help|--help)$' { 'help' }
        '^(?i)(-tag|--tag)$' { 'tag' }
        '^-\+\+\+$' { 'patch' }
        '^-\+\+$' { 'minor' }
        '^-\+$' { 'major' }
        default { throw "Unknown argument: $f`nRun .\newVersion.ps1 -h for usage." }
    }
    switch ($next) {
        'help' { $wantHelp = $true }
        'tag' { $wantTag = $true }
        { $_ -in @('major', 'minor', 'patch') } {
            if ($null -ne $bump -and $bump -ne $next) { throw "Use only one bump at a time (got -$bump and -$next)." }
            $bump = $next
        }
    }
}

if ($wantHelp) {
    if ($wantTag -or $null -ne $bump) { throw "Help cannot be combined with other modes." }
    Show-NewVersionHelp; exit 0
}

if (-not $wantTag -and $null -eq $bump) { Show-NewVersionHelp; exit 0 }

$v = Read-SemVerParts
$major = $v.Major
$minor = $v.Minor
$patch = $v.Patch
$tag = $v.Tag
$build = $v.Build

switch ($bump) {
    'major' { $major++; $minor = 0; $patch = 0 }
    'minor' { $minor++; $patch = 0 }
    'patch' { $patch++ }
}

if ($wantTag) { $tag = (& "$root\.resources\exe\yapCli.exe").Trim(); Write-Host $tag }

if ($null -ne $bump) { Write-Host "Version -> $major.$minor.$patch" }

Write-VersionFile -SemVer "$major.$minor.$patch" -Tag $tag -Build $build; Update-BuildNotesHeader
