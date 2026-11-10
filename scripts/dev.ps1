<#
TransitShelf development entry point.

Usage:
  .\scripts\dev.ps1 build
  .\scripts\dev.ps1 run --project src\TransitShelf.App
  .\scripts\dev.ps1 test

This wrapper prefers the optional repository-local SDK (.dotnet-sdk), honors
global.json, and keeps SDK/NuGet state in the repository (.dotnet-home, .nuget).

IMPORTANT: keep this file ASCII-only. Windows PowerShell 5.1 reads .ps1 files
without a BOM as ANSI (GBK on zh-CN systems), which corrupts non-ASCII text.
#>
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Arguments
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $root '.nuget\packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES | Out-Null

# Order matters: check the real SDK locations before Program Files, because
# "C:\Program Files\dotnet" may hold the runtime only (no sdk/ directory),
# which makes "dotnet build" fail with "No .NET SDKs were found".
$candidates = @(
    (Join-Path $root '.dotnet-sdk\dotnet.exe'),
    'D:\SDK-install\dotnet\dotnet.exe',
    (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'),
    (Join-Path ${env:ProgramFiles} 'dotnet\dotnet.exe')
)

$onPath = Get-Command dotnet.exe -ErrorAction SilentlyContinue
if ($onPath) { $candidates += $onPath.Source }
$dotnet = $null
$requiredSdk = [version] (Get-Content -LiteralPath (Join-Path $root 'global.json') -Raw -Encoding UTF8 | ConvertFrom-Json).sdk.version
$featureBand = $requiredSdk.Build - ($requiredSdk.Build % 100)
Push-Location $root
try {
    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $candidate)) { continue }
        # Listing does not fail when global.json cannot be satisfied by an older host.
        $installedSdks = & $candidate --list-sdks 2>$null
        foreach ($line in $installedSdks) {
            if ($line -notmatch '^(\d+\.\d+\.\d+)\s') { continue }
            $installed = [version] $Matches[1]
            if ($installed.Major -eq $requiredSdk.Major -and $installed.Minor -eq $requiredSdk.Minor -and
                $installed.Build -ge $requiredSdk.Build -and $installed.Build -lt ($featureBand + 100)) {
                $dotnet = $candidate
                break
            }
        }
        if ($dotnet) { break }
    }
}
finally { Pop-Location }
if (-not $dotnet) {
    throw 'Compatible .NET 10 SDK not found. Run scripts\install-sdk.ps1 or install the SDK specified by global.json.'
}

Write-Host "dotnet: $dotnet" -ForegroundColor DarkGray
Push-Location $root
try { & $dotnet @Arguments; $commandExit = $LASTEXITCODE }
finally { Pop-Location }
exit $commandExit
