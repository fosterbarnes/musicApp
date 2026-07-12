# Version helper. Parse $args so -+, -++, -+++ work unquoted in pwsh.
. "$PSScriptRoot\scriptHelper.ps1"

function Show-NewVersionHelp {
    Write-Host @"
  .\newVersion.ps1 -h               Show help.
  .\newVersion.ps1 -tag             Generate a new Version tag (line 2)
  .\newVersion.ps1 -+               Major bump: 1.0.0 -> 2.0.0
  .\newVersion.ps1 -++              Minor bump: 0.1.0 -> 0.2.0
  .\newVersion.ps1 -+++             Patch bump: 0.0.1 -> 0.0.2
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
    foreach ($x in $p) {
        if ($x -notmatch '^\d+$') { throw "Invalid version: $raw" }
    }
    return @{
        Major = [int]$p[0]
        Minor = [int]$p[1]
        Patch = [int]$p[2]
        Tag   = $lines[1]
        Build = $lines[2]
    }
}

function Write-SemVer {
    param([int]$Major, [int]$Minor, [int]$Patch)
    $cur = Read-VersionFileLines
    Write-VersionFile -SemVer "$Major.$Minor.$Patch" -Tag $cur[1] -Build $cur[2]
    Write-Host "Version -> $Major.$Minor.$Patch"
}

$flags = @(
    $args |
        ForEach-Object { "$_".Trim() } |
        Where-Object { $_.Length -gt 0 }
)

if ($flags.Count -eq 0) {
    Show-NewVersionHelp
    exit 0
}

$mode = $null
foreach ($f in $flags) {
    $next = switch -Regex ($f) {
        '^(?i)(-h|--h|-help|--help)$' { 'help' }
        '^(?i)(-tag|--tag)$' { 'tag' }
        '^-\+\+\+$' { 'patch' }
        '^-\+\+$' { 'minor' }
        '^-\+$' { 'major' }
        default { throw "Unknown argument: $f`nRun .\newVersion.ps1 -h for usage." }
    }
    if ($null -ne $mode -and $mode -ne $next) {
        throw "Use only one mode at a time (got -$mode and -$next)."
    }
    $mode = $next
}

switch ($mode) {
    'help' {
        Show-NewVersionHelp
    }
    'tag' {
        $tag = (& "$root\.resources\exe\yapCli.exe").Trim()
        Write-Host $tag
        $cur = Read-VersionFileLines
        Write-VersionFile -SemVer $cur[0] -Tag $tag -Build $cur[2]
        Update-BuildNotesHeader
    }
    'major' {
        $v = Read-SemVerParts
        Write-SemVer -Major ($v.Major + 1) -Minor 0 -Patch 0
        Update-BuildNotesHeader
    }
    'minor' {
        $v = Read-SemVerParts
        Write-SemVer -Major $v.Major -Minor ($v.Minor + 1) -Patch 0
        Update-BuildNotesHeader
    }
    'patch' {
        $v = Read-SemVerParts
        Write-SemVer -Major $v.Major -Minor $v.Minor -Patch ($v.Patch + 1)
        Update-BuildNotesHeader
    }
}
