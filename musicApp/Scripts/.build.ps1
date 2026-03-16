param(
    [switch]$d,
    [switch]$r
)

$config = if ($d) { "Debug" } else { "Release" }
Set-Location (Split-Path -Parent $PSScriptRoot)
dotnet build musicApp.csproj -c $config 