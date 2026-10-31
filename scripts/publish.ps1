<#
Publish a distributable build of TransitShelf (product name: Blossom Rest).

Usage:
  .\scripts\publish.ps1                      # self-contained win-x64, Release
  .\scripts\publish.ps1 -SelfContained:$false # framework-dependent
  .\scripts\publish.ps1 -Output D:\some\dir   # default: <repo>\dist

Outputs:
  <Output>\BlossomRest-win-x64\              portable folder, unzip and run
  <Output>\BlossomRest-<version>-win-x64.zip

The SDK is located by scripts\dev.ps1, so this script never probes for it.
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
    $Output = Join-Path $root 'dist'
}
elseif (-not [System.IO.Path]::IsPathRooted($Output)) {
    $Output = Join-Path $root $Output
}
$Output = [System.IO.Path]::GetFullPath($Output)
$publishDir = Join-Path $Output 'BlossomRest-win-x64'

# Version comes from the app project; fall back when the property is absent.
$version = '0.1.0'
try {
    [xml] $proj = Get-Content -LiteralPath $project
    $versionNode = $proj.Project.PropertyGroup |
        ForEach-Object { $_.Version } |
        Where-Object { $_ } |
        Select-Object -First 1
    if ($versionNode) {
        $version = ([string] $versionNode).Trim()
    }
}
catch {
    Write-Host "WARN: could not read Version from csproj, using $version" -ForegroundColor Yellow
}

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
if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

& $devScript @publishArgs
if ($LASTEXITCODE -ne 0) {
    Fail "dotnet publish failed with exit code $LASTEXITCODE."
}

$exe = Join-Path $publishDir 'TransitShelf.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    Fail "Publish completed but $exe was not found."
}

$pdb = @(Get-ChildItem -LiteralPath $publishDir -Filter '*.pdb' -Recurse -ErrorAction SilentlyContinue)
if ($pdb.Count -gt 0) {
    Fail "Publish output contains $($pdb.Count) .pdb file(s); DebugType=None did not take effect."
}

$zipPath = Join-Path $Output ("BlossomRest-{0}-win-x64.zip" -f $version)
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Write-Host "Compressing to $zipPath ..." -ForegroundColor DarkGray
Compress-Archive -Path $publishDir -DestinationPath $zipPath -CompressionLevel Optimal

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
