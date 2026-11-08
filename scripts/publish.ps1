<#
Publish a distributable build of TransitShelf (product name: Blossom Rest).

Usage:
  .\scripts\publish.ps1                      # self-contained win-x64, Release
  .\scripts\publish.ps1 -SelfContained:$false # framework-dependent
  .\scripts\publish.ps1 -Output D:\some\dir   # new directory only

Outputs:
  <Output>\BlossomRest-win-x64\              portable folder, unzip and run
  <Output>\BlossomRest-<version>-win-x64.zip

The SDK is located by scripts\dev.ps1, so this script never probes for it.
Default output is a new timestamped release directory under <repo>\dist.
Keep this file ASCII-only (see the note in dev.ps1).
#>
param(
    [bool] $SelfContained = $true,
    [string] $Output
)

$ErrorActionPreference = 'Stop'

function Fail([string] $Message) {
    [Console]::Error.WriteLine("ERROR: $Message")
    exit 1
}

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\TransitShelf.App\TransitShelf.App.csproj'
$devScript = Join-Path $PSScriptRoot 'dev.ps1'

if (-not (Test-Path -LiteralPath $project)) {
    Fail "App project not found: $project"
}

if (-not $Output) {
    $Output = Join-Path $root ('dist\release-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
elseif (-not [System.IO.Path]::IsPathRooted($Output)) {
    $Output = Join-Path $root $Output
}
$Output = [System.IO.Path]::GetFullPath($Output)
$publishDir = Join-Path $Output 'BlossomRest-win-x64'

if (Test-Path -LiteralPath $Output) {
    Fail 'Output already exists. Choose a new directory; existing release data will not be replaced.'
}
New-Item -ItemType Directory -Path $Output | Out-Null

$flavor = if ($SelfContained) { 'self-contained' } else { 'framework-dependent' }

$publishArgs = @(
    'publish'
    $project
    '-c', 'Release'
    '-r', 'win-x64'
    '--self-contained', $(if ($SelfContained) { 'true' } else { 'false' })
    '-o', $publishDir
    '-p:DebugType=None'
    '-p:DebugSymbols=false'
    '--nologo'
)

Write-Host "Publishing $flavor win-x64 (Release) to $publishDir" -ForegroundColor DarkGray
& $devScript @publishArgs
if ($LASTEXITCODE -ne 0) {
    Fail "dotnet publish failed with exit code $LASTEXITCODE."
}

$exe = Join-Path $publishDir 'TransitShelf.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    Fail "Publish completed but $exe was not found."
}

# Read the evaluated build version, including inherited Directory.Build.props.
$version = ((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion -split '\+')[0]
if ($version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') {
    Fail "The published executable has an invalid version: $version"
}
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $publishDir 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $root 'docs\USER-GUIDE.md') -Destination (Join-Path $publishDir 'USER-GUIDE.md')
Copy-Item -LiteralPath (Join-Path $root 'docs\RELEASE-NOTES.md') -Destination (Join-Path $publishDir 'RELEASE-NOTES.md')
Copy-Item -LiteralPath (Join-Path $root 'docs\USER-GUIDE.en.md') -Destination (Join-Path $publishDir 'USER-GUIDE.en.md')
Copy-Item -LiteralPath (Join-Path $root 'docs\RELEASE-NOTES.en.md') -Destination (Join-Path $publishDir 'RELEASE-NOTES.en.md')
New-Item -ItemType Directory -Path (Join-Path $publishDir 'images') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'docs\images\shelf-items.en.png') -Destination (Join-Path $publishDir 'images\shelf-items.en.png')
Copy-Item -LiteralPath (Join-Path $root 'docs\images\settings.en.png') -Destination (Join-Path $publishDir 'images\settings.en.png')

# The self-contained Microsoft.NETCore.App runtime carries its own notices.
if ($SelfContained) {
    $runtimePack = Join-Path $root '.nuget\packages\microsoft.netcore.app.runtime.win-x64'
    $runtimeConfig = Get-Content -LiteralPath (Join-Path $publishDir 'TransitShelf.runtimeconfig.json') -Encoding UTF8 -Raw | ConvertFrom-Json
    $coreVersion = ($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq 'Microsoft.NETCore.App' }).version
    $desktopVersion = ($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq 'Microsoft.WindowsDesktop.App' }).version
    if (-not $coreVersion -or -not $desktopVersion) { Fail 'Cannot determine included runtime versions.' }
    $runtime = Join-Path $runtimePack $coreVersion
    $desktopLicense = Join-Path $root ('.nuget\packages\microsoft.windowsdesktop.app.runtime.win-x64\' + $desktopVersion + '\LICENSE')
    if (-not (Test-Path -LiteralPath (Join-Path $runtime 'LICENSE.TXT'))) { Fail 'Matching runtime license not found.' }
    if (-not (Test-Path -LiteralPath $desktopLicense)) { Fail 'Matching desktop runtime license not found.' }
    Copy-Item -LiteralPath (Join-Path $runtime 'LICENSE.TXT') -Destination (Join-Path $publishDir 'DOTNET-LICENSE.txt')
    Copy-Item -LiteralPath $desktopLicense -Destination (Join-Path $publishDir 'DOTNET-DESKTOP-LICENSE.txt')
    $notices = Join-Path $runtime 'THIRD-PARTY-NOTICES.TXT'
    if (-not (Test-Path -LiteralPath $notices)) { Fail 'Runtime third-party notices not found.' }
    Copy-Item -LiteralPath $notices -Destination (Join-Path $publishDir 'DOTNET-THIRD-PARTY-NOTICES.txt')
}

$pdb = @(Get-ChildItem -LiteralPath $publishDir -Filter '*.pdb' -Recurse -ErrorAction SilentlyContinue)
if ($pdb.Count -gt 0) {
    Fail "Publish output contains $($pdb.Count) .pdb file(s); DebugType=None did not take effect."
}

$zipPath = Join-Path $Output ("BlossomRest-{0}-win-x64.zip" -f $version)
Write-Host "Compressing to $zipPath ..." -ForegroundColor DarkGray
Compress-Archive -Path $publishDir -DestinationPath $zipPath -CompressionLevel Optimal
$archiveHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($zipPath + '.sha256', ($archiveHash + ' *' + [IO.Path]::GetFileName($zipPath) + [Environment]::NewLine), [Text.Encoding]::ASCII)

$folderBytes = (Get-ChildItem -LiteralPath $publishDir -Recurse -File |
    Measure-Object -Property Length -Sum).Sum
$zipBytes = (Get-Item -LiteralPath $zipPath).Length
$folderMb = '{0:N1}' -f ($folderBytes / 1MB)
$zipMb = '{0:N1}' -f ($zipBytes / 1MB)

Write-Host ''
Write-Host "Version:  $version ($flavor win-x64)" -ForegroundColor Green
Write-Host "Folder:   $publishDir  ($folderMb MB)"
Write-Host "Archive:  $zipPath  ($zipMb MB)"
Write-Host "Exe:      $exe"
Write-Host ''
