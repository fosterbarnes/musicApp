param(
    [Alias('p')]
    [switch]$Print,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AppLaunchArgs
)

$PrintScc = [bool]$Print
foreach ($a in @($AppLaunchArgs) + @($args)) {
    if ($a -and $a.Trim() -match '^(?i)(--print|--p|-print|-p)$') { $PrintScc = $true }
}

. "$PSScriptRoot\scriptHelper.ps1"; Set-Location $root
$sccTxt = "$root\.md\scc.txt"

$excludeExts = @(
    'md', 'markdown', 'svg', 'json', 'text', 'txt', 'license', 'licence', 'copying',
    'copying3', 'unlicense', 'unlicence', 'license-apache', 'licence-apache',
    'fsproj', 'vcproj', 'vcxproj', 'vcxproj.filters', 'ilproj', 'myapp',
    'props', 'rdlc', 'resx', 'settings', 'sln', 'targets',
    'copyright', 'yaml', 'yml', 'csproj', 'vbproj'
) -join ','

$excludeFiles = @( 'yapBot.json', 'LICENSE' )
$sccArgs = @('.', '--no-size', '--no-complexity', '--no-cocomo', '--ci', '--exclude-ext', $excludeExts)
foreach ($f in $excludeFiles) { $sccArgs += @('--exclude-file', $f) }
& scc @sccArgs | Out-File -FilePath $sccTxt -Encoding utf8

$m = Select-String -Path $sccTxt -Pattern '^\s*Total\s+\d+\s+([\d,]+)' | Select-Object -First 1
$totalLines = $m.Matches[0].Groups[1].Value

Add-Content -Path $sccTxt -Encoding utf8 -Value "$totalLines lines of code and counting..."

$readmeContent = Get-Content -Raw -Path $readme
$pattern = '(?m)^\[([\d,]+)\](\(https://github\.com/fosterbarnes/musicApp/blob/main/\.md/scc\.txt\)\s+lines of code and counting\.\.\.)'
$updated = $readmeContent -replace $pattern, ('[' + $totalLines + ']$2')
if ($updated -ne $readmeContent) {
    Write-RepoUtf8NoBomFile -LiteralPath $readme -Content $updated
}

if ($PrintScc) { Get-Content -Path $sccTxt }
