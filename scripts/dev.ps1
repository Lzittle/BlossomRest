<#
TransitShelf development entry point.

Usage:
  .\scripts\dev.ps1 build
  .\scripts\dev.ps1 run --project src\TransitShelf.App
  .\scripts\dev.ps1 test

Why this wrapper exists: the .NET SDK used by this project lives in the user
profile (%LOCALAPPDATA%\Microsoft\dotnet) and is not added to the system PATH.
It also keeps the SDK/NuGet state inside the repository (.dotnet-home, .nuget).

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

$candidates = @(
    (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'),
    (Join-Path ${env:ProgramFiles} 'dotnet\dotnet.exe')
)

$dotnet = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $dotnet) {
    throw 'dotnet not found. Install the .NET SDK first (see docs/tech-framework section 9).'
}

$sdk = & $dotnet --list-sdks 2>$null
if (-not $sdk) {
    throw "No SDK found under $dotnet (runtime only). Install the .NET SDK first."
}

Write-Host "dotnet: $dotnet" -ForegroundColor DarkGray
& $dotnet @Arguments
exit $LASTEXITCODE
